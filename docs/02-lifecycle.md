# Жизненный цикл

```
Created → Configured → Scheduled → Running → Completed
```

Состояния заданы enum `IntentLifecycle` и меняются на `Intent` / `Intent<T>`.

## Created

Появляется при:

- вызове `async Intent Foo()` — builder создаёт экземпляр и **биндит** state machine, но **не** вызывает `MoveNext`;
- `Intent.Run(...)` / `Defer(...)` — создаётся экземпляр с телом-делегатом.

На этой стадии:

- побочных эффектов пользовательского кода ещё нет;
- политики ещё не обязательны;
- объект уже можно передать дальше, положить в список, обернуть.

## Configured

После первого `Configure(...)`:

```csharp
intent.Configure(Intent.Retry(3), Intent.Timeout(5.Seconds()));
```

Политики накапливаются в списке. Повторный `Configure` до старта — дополняет список.  
`Configure` **после** `Scheduled`/`Running`/`Completed` бросает `InvalidOperationException`.

## Scheduled

Первый `GetAwaiter()` (то есть начало `await intent`) вызывает `Schedule()`:

1. CAS-флаг `_scheduleGate` — повторный schedule no-op.
2. Lifecycle → `Scheduled`.
3. Запускается `RunPipelineAsync` (fire-and-forget на thread pool через async state machine самого pipeline).

Важно: **ещё не обязательно** бежит код пользователя — сначала строится и запускается pipeline.

## Running

Внутри `RunPipelineAsync`:

1. Lifecycle → `Running`.
2. `IntentPipeline.Build(policies, body)` собирает цепочку обёрток.
3. `await pipeline(CancellationToken.None)`.

Тело пользователя выполняется как самый внутренний `next` в этой цепочке.

## Completed

Успех или ошибка:

- успех → `TaskCompletionSource` получает result (для `Intent<T>` — значение);
- ошибка → exception в TCS (+ `ExceptionDispatchInfo` для корректного rethrow в `GetResult`).

После `Completed` повторный `await` просто ждёт уже завершённый TCS (тело **не** перезапускается). Для повторов нужна политика `Retry` (она перезапускает body до завершения) или новый `Intent`.

## Когда код пользователя ещё не бежит

До `Scheduled` (фактически до входа pipeline в body):

| Действие | Код пользователя |
|----------|------------------|
| `var x = ProcessOrder()` | Нет |
| `x.Configure(...)` | Нет |
| передать `x` в другой метод | Нет |
| `await x` | Да (через pipeline) |

Именно это отличает Intent от обычного `async Task`: там тело стартует **в момент вызова метода**.
