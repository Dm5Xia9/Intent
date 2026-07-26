# Intent vs Task

## Коротко

| | `Task` / `async Task` | `Intent` / `async Intent` |
|--|------------------------|---------------------------|
| Когда стартует тело | При **вызове** метода | При **await** (schedule) |
| Что возвращает вызов | Уже бегущую (или завершённую) операцию | Холодный план |
| Политики исполнения | Снаружи или внутри вручную | `With*` / `Configure` + pipeline |
| Свой scheduler | Thread pool / sync context / custom | Тот же CLR: поверх `Task` + TCS |
| Повтор выполнения | Нужен новый вызов метода | `Retry` (клонирует body / state machine) |

`Intent` **не заменяет** планировщик задач .NET. Он меняет момент старта и добавляет слой политик.

## Что происходит при вызове `async Task`

Компилятор генерирует state machine и `AsyncTaskMethodBuilder`. Типичный поток:

1. `Create()` builder.
2. Инициализация SM, `builder` копируется в поле SM.
3. `builder.Start(ref sm)` → **сразу** `sm.MoveNext()`.
4. Тело бежит до первого `await` незавершённого awaitable.
5. Наружу возвращается `builder.Task` — уже «горячий» `Task`.

Поэтому:

```csharp
var t = LoadAsync(); // HTTP-запрос уже мог уйти
await t;
```

## Что происходит при вызове `async Intent`

Используется `[AsyncMethodBuilder(typeof(IntentMethodBuilder))]`.

1. `Create()` — сразу создаётся экземпляр `Intent` (чтобы все копии struct-builder’а делили один reference).
2. SM получает копию builder’а с тем же `_intent`.
3. `Start(ref sm)`:
   - боксит state machine;
   - `BindStateMachine(boxed)`;
   - **не** вызывает `MoveNext`.
4. Наружу возвращается холодный `Intent`.

```csharp
var intent = LoadAsIntent(); // сеть ещё не тронута
intent = intent.WithTimeout(2.Seconds());
await intent; // только здесь MoveNext → реальная работа
```

## Await и «планировщик»

### Task

- Продолжения после `await` идут через `SynchronizationContext` / `TaskScheduler` / thread pool.
- `ConfigureAwait(false)` отвязывает от sync context.
- Сам `Task` — единица работы runtime’а (с исключениями для `ValueTask` и синхронного завершения).

### Intent

- Снаружи Intent — обычный awaitable (`GetAwaiter` → `ICriticalNotifyCompletion`).
- Внутри завершения сигнал идёт через `TaskCompletionSource` с `RunContinuationsAsynchronously`.
- Когда пользовательский код внутри Intent делает `await Task.Delay(...)`, продолжение SM снова идёт через **стандартный** механизм Task/awaiter.

Итого: Intent **не** пишет свой thread pool и **не** подменяет `TaskScheduler`. Он:

1. откладывает первый `MoveNext`;
2. прогоняет выполнение через pipeline;
3. стыкует результат с внешним await через TCS.

## Горячий vs холодный

```
Task:    call ──► running ──► await (join)
Intent:  call ──► plan ──► configure ──► await ──► running
```

Холодность критична для:

- единообразного навешивания политик **до** старта;
- отсутствия гонок «уже побежало, а timeout ещё не повесили»;
- композиции (`FromFactory`, списки намерений, условный запуск).

## Повторы

`Task` после faulted/completed мёртв для «прогона заново».  
`Intent` с `Retry` повторно вызывает body: для делегатов — тот же `Func`, для `async Intent` — **клон** ещё не запущенной state machine (шаблон сохраняется при создании плана).

```csharp
await Flaky().WithRetry(3);
```

`FromFactory` нужен только если фабрика сама должна выполняться заново (побочные эффекты вне тела Intent), а не для обычного Retry.

Полный контракт (captures, nested await, double-await, cancel/timeout): **[09-sm-clone-contract.md](09-sm-clone-contract.md)**.

## Когда оставить Task

- Обычный I/O, UI, ASP.NET handlers без политики «снаружи».
- Библиотечный код, где контракт мира — `Task`/`ValueTask`.
- Горячий fire-and-forget (`_ = DoAsync()`), если именно это нужно.

## Когда взять Intent

- Операция — **единица бизнес-действия**, которой нужны режимы исполнения.
- Хочется CQS-дружелюбных мелких методов + атомарность снаружи.
- Один код — разные профили: «в UI без ретрая», «в фоне с timeout+retry».
