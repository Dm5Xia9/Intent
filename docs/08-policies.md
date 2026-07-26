# Политики Intent

Политика — реализация `IntentPolicy`: обёртка вокруг следующего слоя исполнения.

```csharp
public interface IntentPolicy
{
    int Order { get; } // меньше = снаружи
    Func<CancellationToken, Task> Wrap(Func<CancellationToken, Task> next);
}
```

Предпочтительный способ навешивания — **builder API** (`With*` / `WithPolicies`). `Configure(...)` остаётся низкоуровневым API для массива `IntentPolicy`.

```csharp
using Intents.Polly;
using Intents.Time;

await work
    .WithRetry(3)
    .WithTimeout(5.Seconds());

// низкоуровнево:
await work.Configure(IntentPolly.Retry(3), IntentPolly.Timeout(5.Seconds()));
```

Порядок в `With*` / `Configure` **не важен** — pipeline сортирует по `Order`. Общая схема: [03-pipeline.md](03-pipeline.md).

Resilience (Retry/Timeout/CB/Bulkhead) — пакет **Intent.Polly** (Polly v8).

Можно также: `work.WithResilience(myPipeline)`.

Ниже — каждая политика: зачем, API, поведение, ограничения.

---

## Сводная таблица

| Политика | Order | API | Пакет |
|----------|-------|-----|-------|
| Cancel | -20 | `IntentPolicies.Cancel` / `WithCancel` | core |
| Named | -15 | `IntentPolicies.Named` / `WithNamed` | core |
| Tag | -14 | `IntentPolicies.Tag` / `WithTag` / `WithTags` | core |
| Trace | -10 | `IntentPolicies.Trace` / `WithTrace` | core |
| Activity | -8 | `IntentPolicies.Activity` / `WithActivity` | core |
| Metrics | -5 | `IntentPolicies.Metrics` / `WithMetrics` | core |
| Idempotent | -4 | `IntentPolicies.Idempotent` / `WithIdempotent` | core |
| Cache | -3 | `IntentPolicies.Cache` / `WithCache` | core |
| CircuitBreaker | -2 | `IntentPolly.CircuitBreaker` / `WithCircuitBreaker` | Intent.Polly |
| Timeout | 0 | `IntentPolly.Timeout` / `WithTimeout` | Intent.Polly |
| Retry | 1 | `IntentPolly.Retry` / `WithRetry` | Intent.Polly |
| Bulkhead | 2 | `IntentPolly.Bulkhead` / `WithBulkhead` | Intent.Polly |
| Atomic | 3 | `IntentPolicies.Atomic` / `WithAtomic` / `WithAtomicOn` | core |
| Before | 4 | `IntentPolicies.Before` / `WithBefore` | core |
| After | 5 | `IntentPolicies.After` / `WithAfter` | core |

---

## Cancel (`Order -20`)

**Зачем.** Пробросить внешний `CancellationToken` в весь pipeline (и в тело, если оно его принимает).

```csharp
await Intent.From(async ct => await client.GetAsync(url, ct))
    .WithCancel(ct)
    .WithTimeout(5.Seconds());
```

**Поведение.** Создаёт linked CTS из входящего token pipeline и вашего `ct`, прокидывает его в `next` и в `Intent.CurrentCancellationToken`. Самый внешний слой — отмена видна Timeout, Retry, Bulkhead, Atomic и body.

**Важно.**
- `Intent.From(Action)` / `From(Func<Task>)` token почти не используют (только проверка в sync `From(Action)`).
- Для реальной отмены I/O пишите `From(async ct => …)` или `async Intent` с `await Something(ct)`.
- Отмена обычно даёт `OperationCanceledException`; Retry по умолчанию её **не** повторяет.

---

## Named (`Order -15`)

**Зачем.** Дать операции человекочитаемое имя для Trace / Activity.

```csharp
await work.WithNamed("Checkout").WithActivity();
```

**Поведение.** Кладёт имя в `IntentAmbient` на время выполнения вложенного pipeline, затем восстанавливает предыдущее.

**Важно.** Без `Named` диагностики используют имя `"Intent"`. Имеет смысл ставить **до** Trace/Activity (Order уже гарантирует это).

---

## Tag (`Order -14`)

**Зачем.** Прокинуть baggage (userId, orderId, …) в Activity / Trace на время операции.

```csharp
await work
    .WithNamed("Checkout")
    .WithTag("userId", userId)
    .WithTags(("orderId", orderId), ("region", "eu"))
    .WithActivity()
    .WithTrace();
```

**Поведение.** Мержит теги в ambient-словарь (`IntentAmbient`), восстанавливает предыдущий при выходе. `Activity` получает теги при старте; `IntentTraceEvent` несёт снимок в поле `Tags`.

**Важно.** Process-local ambient через `AsyncLocal`. Вложенные `Tag` накладываются поверх внешних.

---

## Trace (`Order -10`)

**Зачем.** Лёгкие события жизненного цикла операции без привязки к OpenTelemetry.

```csharp
IntentDiagnostics.Traced += e => Console.WriteLine($"{e.Phase} {e.Name} {e.Duration}");

await work.WithNamed("Sync").WithTrace();
```

**События** (`IntentTraceEvent`):
- `Started` — перед `next`
- `Succeeded` — после успешного `next` (+ duration)
- `Faulted` — при исключении (+ duration, exception)

Подписка: `IntentDiagnostics.Traced`. Сброс: `IntentDiagnostics.Reset()`.

**Vs Activity.** Trace — простой callback. Activity — стандартный `System.Diagnostics.Activity` для OTel/APM.

---

## Activity (`Order -8`)

**Зачем.** OpenTelemetry-совместимый span через `IntentInstrumentation.ActivitySource` (`Intents.Intent`).

```csharp
await work.WithNamed("Pay").WithTag("userId", id).WithActivity();
```

**Поведение.**
- Наследует `Activity.Current` (parent-child в Aspire / Jaeger).
- Стартует Activity с именем из ambient (`Named` или `"Intent"`).
- Ambient **Tag** → span tags **и** baggage.
- Успех → `ActivityStatusCode.Ok`; ошибка → `Error` + `exception.*`.

Слушать: `ActivityListener` / OTel с source `Intents.Intent`. Гайд: **[10-observability.md](10-observability.md)**.

---

## Metrics (`Order -5`)

**Зачем.** Счётчики и гистограммы через `System.Diagnostics.Metrics` (OTel / Aspire / Prometheus).

```csharp
await work.WithNamed("Pay").WithMetrics();
```

| Instrument | Тип | Имя |
|------------|-----|-----|
| Count | Counter | `intents.execution.count` |
| Duration | Histogram (ms) | `intents.execution.duration` |

Tags: `intent.name`, `intent.outcome` (`success`|`failure`) + ambient Tag.

Meter name: **`Intents.Intent`** (`IntentInstrumentation.Name`).

Регистрация в OTel: `.AddMeter(IntentInstrumentation.Name)`. Подробности: **[10-observability.md](10-observability.md)**.

---

## Idempotent (`Order -4`)

**Зачем.** Дедуп по ключу: параллельные вызовы делят один run; успешный результат помнится на TTL. Ошибка **не** залипает — следующий вызов может повторить.

```csharp
await Intent.From(() => Charge(cmd))
    .WithIdempotent($"pay:{cmd.IdempotencyKey}", TimeSpan.FromHours(24));

var status = await Intent.From(() => CreateOrder(cmd))
    .WithIdempotent($"order:{cmd.Key}");
```

**Поведение.**
1. Есть незавершённый run с тем же ключом → ждать его (успех или та же ошибка).
2. Есть успешный результат в TTL → вернуть его / no-op для `Intent`, тело не бежит.
3. Иначе выполнить pipeline; при успехе сохранить; при ошибке ключ освободить.

Default TTL = 1 час. Process-local store (`IntentIdempotencyStore.Clear()` в тестах).

**Vs Cache.** Cache — «просто ускорить чтение». Idempotent — «не выполнить side effect дважды» + in-flight join. Можно комбинировать: Idempotent снаружи.

---

## Cache (`Order -3`)

**Зачем.** In-memory кеш результата для **`Intent<T>`**.

```csharp
var profile = await Intent.From(() => Load(userId))
    .WithCache($"user:{userId}", 30.Seconds());
```

**Поведение.**
1. До pipeline: hit → сразу `CompleteOuter` с закешированным значением (тело **не** бежит).
2. Miss → обычный pipeline; после успеха значение кладётся в store с TTL.

На `Intent` без результата политика в Wrap — no-op (кешировать нечего).

**Важно.**
- Process-local `ConcurrentDictionary`, не Redis.
- Не для side-effect операций (POST/списание).
- Hit не проходит через CircuitBreaker/Timeout/Retry — это задумано (Cache снаружи).

---

## CircuitBreaker (`Order -2`)

**Зачем.** Fail-fast, когда зависимость уже «лежит».

```csharp
await Intent.From(CallPayments)
    .WithCircuitBreaker("payments", failureThreshold: 5, breakDuration: 30.Seconds());
```

**Состояния.**
| Состояние | Поведение |
|-----------|-----------|
| Closed | Обычный вызов; ошибки копятся |
| Open | Сразу `BrokenCircuitException` (Polly) до истечения `breakDuration` |
| Half-open | После паузы — один probe; успех → Closed, ошибка → Open снова |

`OperationCanceledException` **не** считается failure для открытия circuit.

Сброс (тесты): `IntentPolly.ResetNamedPipelines()`. State — внутри Polly `ResiliencePipelineRegistry`.

**Важно.** Process-local через Polly registry. Threshold/break задаются при первом создании по `name`.

---

## Timeout (`Order 0`)

**Зачем.** Общий wall-time бюджет на весь внутренний pipeline (включая Retry).

```csharp
await work.WithTimeout(10.Seconds()).WithRetry(5);
```

**Поведение.** Linked CTS с `CancelAfter` + `WaitAsync` (wall-clock). При превышении — `TimeoutRejectedException`. Token прокидывается в тело (`From(async ct => …)` / `Intent.CurrentCancellationToken`).

**Важно.**
- Снаружи Retry → все попытки делят один бюджет.
- Не abort’ит поток: без cooperative cancel тело может продолжить работу после timeout на уровне ожидания.
- Для лимита **на попытку** используйте `Retry(..., attemptTimeout: …)`.
- Полный контракт: [09-sm-clone-contract.md](09-sm-clone-contract.md).

---

## Retry (`Order 1`)

**Зачем.** Повторить тело при временных сбоях.

```csharp
await work.WithRetry(
    attempts: 5,
    backoff: IntentBackoff.Exponential(100.Milliseconds()),
    shouldRetry: ex => ex is HttpRequestException or TimeoutException,
    attemptTimeout: 2.Seconds());
```

**Параметры.**
| | |
|--|--|
| `attempts` | Число попыток (≥ 1), включая первую |
| `backoff` | Задержка **перед** попыткой `i` (`i` = 1.. для ретраев). `null` = без паузы |
| `shouldRetry` | Фильтр исключений. По умолчанию: всё, кроме `OperationCanceledException` |
| `attemptTimeout` | Polly Timeout **внутри** Retry на каждую попытку |

Для `async Intent` каждая попытка — **клон** state machine (шаблон не мутируется). `FromFactory` для Retry не обязателен.

**Backoff.**
- `IntentBackoff.Constant(delay)`
- `IntentBackoff.Exponential(initial, factor: 2, max: …)`

---

## Bulkhead (`Order 2`)

**Зачем.** Ограничить параллелизм для класса операций (не дать одному типу I/O забить процесс).

```csharp
await Intent.From(CallHttp)
    .WithBulkhead("http", maxParallelism: 32);
```

**Поведение.** Polly `AddConcurrencyLimiter` (пакет `Polly.RateLimiting`). Именованный pipeline в registry.

**Vs Atomic.**
| | Bulkhead | Atomic |
|--|----------|--------|
| Семантика | До N параллельных | Ровно 1 |
| Ключ | Имя пула | Global или `AtomicOn(key)` |

Если для того же `name` создать Bulkhead с другим `max`, побеждает **первый** зарегистрированный pipeline.

---

## Atomic / AtomicOn (`Order 3`)

**Зачем.** Критическая секция in-process.

```csharp
await work.WithAtomic();           // один глобальный лок
await work.WithAtomicOn("order:1");

await Intent.Atomically(() => { /* ... */ });
await Intent.Atomically("wallet:9", () => Debit(9));
```

**Поведение.** `SemaphoreSlim(1,1)` — глобальный или per-key. Внутри Retry/Bulkhead, снаружи Before/After/body: лок не держится на время backoff Retry и ожидания Timeout снаружи, но охватывает хуки и тело.

**Важно.** Не распределённый лок и не транзакция БД. Разные ключи не блокируют друг друга; один ключ — взаимное исключение.

---

## Before (`Order 4`) / After (`Order 5`)

**Зачем.** Хуки вокруг **каждой попытки** тела Intent (внутри Retry).

```csharp
await Intent.From(DoWork)
    .WithRetry(3)
    .WithBefore(async () => await PrepareAttemptAsync())
    .WithAfter(async () => await CleanupAttemptAsync());
```

**Поведение.**
- `Before` — перед телом (после Atomic, если есть).
- `After` — в `finally` после тела: и при успехе, и при ошибке попытки.
- Оба внутри Retry → на каждую попытку заново.
- Overload’ы: `Action`, `Func<Task>`, `Func<CancellationToken, Task>`.

**Важно.** Это не замена тела метода и не middleware «один раз на весь pipeline снаружи Retry». Для однократного setup на всю операцию используйте свой `IntentPolicy` с меньшим `Order` (снаружи Retry).

---

## Что не политика, но рядом

| API | Роль |
|-----|------|
| `Intent.WhenAll` | Параллельный запуск нескольких Intent |
| `Intent.Sequence` | Последовательный запуск |
| `Background()` | Schedule без await; faults → `IntentDiagnostics` |
| `Into(channel)` / `Into(writer)` | Background + запись результата `Intent{T}` в channel |
| `FromEach(body, ct)` / `FromEach(body, ct, into:)` | Background consumer: Intent на каждый item; `CancellationToken` обязателен |
| `Then` / `Select` | Цепочка по результату `Intent<T>` |
| `IntentPolly.Http` / `DbWrite` | Готовые packs (resilience + Named/Activity/Atomic) |

Политики на композите оборачивают **весь** агрегат, не каждого ребёнка. Ретрай/timeout на каждый child — вешайте `With*` на сами `a`, `b`.

---

## Как выбрать набор

```
Нужна отмена извне?           → Cancel + From(async ct => …)
Нужна трасса / лёгкие метрики? → Named + Tag + Trace/Activity/Metrics
Чтение без side effects?      → Cache
Side effect + Retry?          → Idempotent(key)
Внешний сервис?               → IntentPolly.Http / CB+Bulkhead+Retry+Timeout
Критическая секция / DB write?→ IntentPolly.DbWrite / AtomicOn
Хуки на каждую попытку?       → Before / After
Общий бюджет времени?         → Timeout
Лимит на одну попытку?        → Retry(..., attemptTimeout:)
```

Типичный I/O-профиль:

```csharp
.WithPolicies(IntentPolly.Http("Fetch"), IntentPolicies.Cancel(ct))

// или вручную:
.WithNamed("Fetch")
.WithActivity()
.WithMetrics()
.WithCircuitBreaker("api")
.WithBulkhead("api", 16)
.WithRetry(3, IntentBackoff.Exponential(100.Milliseconds()), attemptTimeout: 1.Seconds())
.WithTimeout(5.Seconds())
.WithCancel(ct)
```
