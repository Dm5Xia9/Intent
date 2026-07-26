# Идея Intent

## Одна фраза

`Intent` — это **описание намерения выполнить работу**, а не сама работа.

Вызов метода создаёт план. План можно настроить политиками (`With*` / `Configure`). Реальное выполнение начинается только при `await` (или явном `Schedule`).

```csharp
using Intents.Polly;

var plan = ProcessOrder();              // Created: код ProcessOrder ещё не бежал
plan = plan.WithRetry(3);               // Configured: политики прикреплены
await plan;                             // Scheduled → Running → Completed
```

## Зачем отделять «что» от «как»

В обычном C# вызов `async Task`-метода **сразу** запускает state machine. Политики (retry, timeout, изоляция) приходится либо:

- вшивать внутрь метода (`try/catch` + циклы),
- оборачивать снаружи (`pipeline.ExecuteAsync(() => ProcessOrder())`),
- плодить builder-API.

`Intent` делает другое:

1. Метод описывает **бизнес-шаги** (validate → save → notify).
2. Вызывающий (или композитор) описывает **режим исполнения** (atomic, retry, timeout…).
3. Один и тот же `ProcessOrder` можно запустить «просто», «с тремя ретраями» или «атомарно + с таймаутом» — **не меняя тело метода**.

Это близко к идее effect systems / command objects / cold observables, но в идиоматичном C# с `async`/`await`.

## Что Intent делает сейчас

| Возможность | Статус |
|-------------|--------|
| Отложенный `async Intent` / `async Intent<T>` | Есть (core) |
| `With*` / `Configure` + нормализованный pipeline | Есть (core) |
| Cancel, Named, Tag, Trace, Activity, Metrics, Idempotent, Cache, Atomic, Before/After | Есть (core) |
| WhenAll, Sequence, Background, Into, FromEach, Then/Select | Есть (core) |
| Retry, Timeout, CircuitBreaker, Bulkhead | Пакет **Intent.Polly** (Polly v8) |
| Свой планировщик потоков/очередей | Нет — опирается на `Task` + thread pool |
| Распределённые транзакции / remote | Вне scope |

## Чем это не является

- **Не** замена `Task` для всего async-кода в приложении.
- **Не** полноценный job scheduler (Hangfire, Quartz).
- **Не** actor framework и не distributed saga engine.

Resilience (retry/timeout/CB/bulkhead) подключается пакетом `Intent.Polly` (Polly v8).

Это тонкий слой **над** механизмом awaitables CLR: cold execution + policy pipeline.

## Главный принцип

```
Пользователь:  что сделать
Intent:        cold plan, lifecycle, composition, local policies
Intent.Polly:  resilience (Polly v8)
```
