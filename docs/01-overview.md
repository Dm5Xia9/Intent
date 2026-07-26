# Идея Intent

## Одна фраза

`Intent` — это **описание намерения выполнить работу**, а не сама работа.

Вызов метода создаёт план. План можно настроить политиками (`With*` / `Configure`). Реальное выполнение начинается только при `await` (или явном `Schedule`).

```csharp
var plan = ProcessOrder();              // Created: код ProcessOrder ещё не бежал
plan = plan.WithRetry(3);               // Configured: политики прикреплены
await plan;                             // Scheduled → Running → Completed
```

## Зачем отделять «что» от «как»

В обычном C# вызов `async Task`-метода **сразу** запускает state machine. Политики (retry, timeout, изоляция) приходится либо:

- вшивать внутрь метода (`try/catch` + циклы),
- оборачивать снаружи (`Polly.ExecuteAsync(() => ProcessOrder())`),
- плодить builder-API (`.WithRetry().WithTimeout()...`).

`Intent` делает другое:

1. Метод описывает **бизнес-шаги** (validate → save → notify).
2. Вызывающий (или композитор) описывает **режим исполнения** (atomic, retry, timeout…).
3. Один и тот же `ProcessOrder` можно запустить «просто», «с тремя ретраями» или «атомарно + с таймаутом» — **не меняя тело метода**.

Это близко к идее effect systems / command objects / cold observables, но в идиоматичном C# с `async`/`await`.

## Что Intent делает сейчас (MVP)

| Возможность | Статус |
|-------------|--------|
| Отложенный `async Intent` / `async Intent<T>` | Есть |
| `Configure` + нормализованный pipeline | Есть |
| Retry, Timeout, Atomic / AtomicOn, Cancel, Bulkhead, CircuitBreaker | Есть |
| Named, Trace, Activity, Metrics, Cache | Есть |
| WhenAll, Sequence, Background | Есть |
| Свой планировщик потоков/очередей | Нет — опирается на `Task` + thread pool |
| Распределённые транзакции / remote | Вне scope |

## Чем это не является

- **Не** замена `Task` для всего async-кода в приложении.
- **Не** полноценный job scheduler (Hangfire, Quartz).
- **Не** actor framework и не distributed saga engine.

Это тонкий слой **над** механизмом awaitables CLR: cold execution + policy pipeline.

## Главный принцип

```
Пользователь:  что сделать
Intent:        как выполнить
```

Одна модель исполнения вместо разрозненных механизмов «вручную»: локальные локи, ретраи, таймауты.
