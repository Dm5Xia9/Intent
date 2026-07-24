# Политики Intent

Политика — реализация `IntentPolicy`: обёртка вокруг следующего слоя исполнения.

```csharp
public interface IntentPolicy
{
    int Order { get; } // меньше = снаружи
    Func<CancellationToken, Task> Wrap(Func<CancellationToken, Task> next);
}
```

Навешивание:

```csharp
await work.Useful(Intent.Retry(3), Intent.Timeout(5.Seconds()));
```

Порядок в `Useful` **не важен** — pipeline сортирует по `Order`. Общая схема: [03-pipeline.md](03-pipeline.md).

Ниже — каждая встроенная политика: зачем, API, поведение, ограничения.

---

## Сводная таблица

| Политика | Order | API |
|----------|-------|-----|
| Cancel | -20 | `Intent.Cancel(ct)` |
| Named | -15 | `Intent.Named(name)` |
| Tag | -14 | `Intent.Tag(key, value)` / `Tags(...)` |
| Trace | -10 | `Intent.Trace` |
| Activity | -8 | `Intent.Activity` |
| Metrics | -5 | `Intent.Metrics` |
| Idempotent | -4 | `Intent.Idempotent(key, ttl?)` |
| Cache | -3 | `Intent.Cache(key, ttl)` |
| CircuitBreaker | -2 | `Intent.CircuitBreaker(name, …)` |
| Timeout | 0 | `Intent.Timeout(span)` |
| Retry | 1 | `Intent.Retry(n, …)` |
| Bulkhead | 2 | `Intent.Bulkhead(name, max)` |
| Atomic | 3 | `Intent.Atomic` / `AtomicOn(key)` |

---

## Cancel (`Order -20`)

**Зачем.** Пробросить внешний `CancellationToken` в весь pipeline (и в тело, если оно его принимает).

```csharp
await Intent.Run(async ct => await client.GetAsync(url, ct))
    .Useful(Intent.Cancel(ct), Intent.Timeout(5.Seconds()));
```

**Поведение.** Создаёт linked CTS из входящего token pipeline и вашего `ct`. Самый внешний слой — отмена видна Timeout, Retry, Bulkhead, Atomic и body.

**Важно.**
- `Intent.Run(Action)` / `Run(Func<Task>)` token почти не используют (только проверка в sync `Run(Action)`).
- Для реальной отмены I/O пишите `Run(async ct => …)` или `async Intent` с `await Something(ct)`.
- Отмена обычно даёт `OperationCanceledException`; Retry по умолчанию её **не** повторяет.

---

## Named (`Order -15`)

**Зачем.** Дать операции человекочитаемое имя для Trace / Activity / Metrics.

```csharp
await work.Useful(Intent.Named("Checkout"), Intent.Metrics, Intent.Activity);
```

**Поведение.** Кладёт имя в `IntentAmbient` на время выполнения вложенного pipeline, затем восстанавливает предыдущее.

**Важно.** Без `Named` диагностики используют имя `"Intent"`. Имеет смысл ставить **до** Trace/Activity/Metrics (Order уже гарантирует это).

---

## Tag (`Order -14`)

**Зачем.** Прокинуть baggage (userId, orderId, …) в Activity / Trace / Metrics на время операции.

```csharp
await work.Useful(
    Intent.Named("Checkout"),
    Intent.Tag("userId", userId),
    Intent.Tags(("orderId", orderId), ("region", "eu")),
    Intent.Activity,
    Intent.Trace);
```

**Поведение.** Мержит теги в ambient-словарь (`IntentAmbient`), восстанавливает предыдущий при выходе. `Activity` получает теги при старте; `IntentTraceEvent` / `IntentMetricEvent` несут снимок в поле `Tags`.

**Важно.** Process-local ambient через `AsyncLocal`. Вложенные `Tag` накладываются поверх внешних.

---

## Trace (`Order -10`)

**Зачем.** Лёгкие события жизненного цикла операции без привязки к OpenTelemetry.

```csharp
IntentDiagnostics.Traced += e => Console.WriteLine($"{e.Phase} {e.Name} {e.Duration}");

await work.Useful(Intent.Named("Sync"), Intent.Trace);
```

**События** (`IntentTraceEvent`):
- `Started` — перед `next`
- `Succeeded` — после успешного `next` (+ duration)
- `Faulted` — при исключении (+ duration, exception)

Подписка: `IntentDiagnostics.Traced`. Сброс: `IntentDiagnostics.Reset()`.

**Vs Activity.** Trace — простой callback. Activity — стандартный `System.Diagnostics.Activity` для OTel/APM.

---

## Activity (`Order -8`)

**Зачем.** OpenTelemetry-совместимый span через `ActivitySource("Intents.Intent")`.

```csharp
await work.Useful(Intent.Named("Pay"), Intent.Activity);
```

**Поведение.**
- Стартует Activity с именем из ambient (`Named` или `"Intent"`).
- Успех → `ActivityStatusCode.Ok`.
- Ошибка → `Error` + теги `exception.type` / `exception.message`.

Слушать: `ActivityListener` с фильтром по имени source `Intents.Intent`.

---

## Metrics (`Order -5`)

**Зачем.** Агрегированные счётчики process-local.

```csharp
await work.Useful(Intent.Named("Pay"), Intent.Metrics);

IntentMetrics.GetCount("Pay");
IntentMetrics.GetSuccesses("Pay");
IntentMetrics.GetFailures("Pay");
IntentMetrics.GetAverageMilliseconds("Pay");
```

**Поведение.** После завершения `next` (успех или нет) пишет длительность в `IntentMetrics` и шлёт `IntentDiagnostics.Measured`.

`finally` считает и success, и failure — удобно для latency; смотрите `GetSuccesses` / `GetFailures` раздельно.

Сброс: `IntentMetrics.Reset()`.

---

## Idempotent (`Order -4`)

**Зачем.** Дедуп по ключу: параллельные вызовы делят один run; успешный результат помнится на TTL. Ошибка **не** залипает — следующий вызов может повторить.

```csharp
await Intent.Run(() => Charge(cmd))
    .Useful(Intent.Idempotent($"pay:{cmd.IdempotencyKey}", TimeSpan.FromHours(24)));

var status = await Intent.Run(() => CreateOrder(cmd))
    .Useful(Intent.Idempotent($"order:{cmd.Key}"));
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
var profile = await Intent.Run(() => Load(userId))
    .Useful(Intent.Cache($"user:{userId}", 30.Seconds()));
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
await Intent.Run(CallPayments)
    .Useful(Intent.CircuitBreaker("payments", failureThreshold: 5, breakDuration: 30.Seconds()));
```

**Состояния.**
| Состояние | Поведение |
|-----------|-----------|
| Closed | Обычный вызов; ошибки копятся |
| Open | Сразу `IntentCircuitOpenException` до истечения `breakDuration` |
| Half-open | После паузы — один probe; успех → Closed, ошибка → Open снова |

`OperationCanceledException` **не** считается failure для открытия circuit.

Сброс: `CircuitBreakerPolicy.Reset(name)` / `ResetAll()`.

**Важно.** Process-local. Threshold/break задаются при создании политики; состояние общее по `name` в процессе.

---

## Timeout (`Order 0`)

**Зачем.** Общий wall-time бюджет на весь внутренний pipeline (включая Retry).

```csharp
await work.Useful(Intent.Timeout(10.Seconds()), Intent.Retry(5));
```

**Поведение.** `next.WaitAsync(timeout, ct)`. При превышении — `TimeoutException`.

**Важно.**
- Снаружи Retry → все попытки делят один бюджет.
- Не abort’ит поток: без cooperative cancel тело может продолжить работу после timeout на уровне ожидания.
- Для лимита **на попытку** используйте `Retry(..., attemptTimeout: …)`.

---

## Retry (`Order 1`)

**Зачем.** Повторить тело при временных сбоях.

```csharp
await work.Useful(Intent.Retry(
    attempts: 5,
    backoff: IntentBackoff.Exponential(100.Milliseconds()),
    shouldRetry: ex => ex is HttpRequestException or TimeoutException,
    attemptTimeout: 2.Seconds()));
```

**Параметры.**
| | |
|--|--|
| `attempts` | Число попыток (≥ 1), включая первую |
| `backoff` | Задержка **перед** попыткой `i` (`i` = 1.. для ретраев). `null` = без паузы |
| `shouldRetry` | Фильтр исключений. По умолчанию: всё, кроме `OperationCanceledException` |
| `attemptTimeout` | `WaitAsync` на **каждую** попытку отдельно |

Для `async Intent` каждая попытка — **клон** state machine (шаблон не мутируется). `Defer` для Retry не обязателен.

**Backoff.**
- `IntentBackoff.Constant(delay)`
- `IntentBackoff.Exponential(initial, factor: 2, max: …)`

---

## Bulkhead (`Order 2`)

**Зачем.** Ограничить параллелизм для класса операций (не дать одному типу I/O забить процесс).

```csharp
await Intent.Run(CallHttp)
    .Useful(Intent.Bulkhead("http", maxParallelism: 32));
```

**Поведение.** Именной `SemaphoreSlim(max, max)`. Слот занимается на время `next`, затем отпускается.

**Vs Atomic.**
| | Bulkhead | Atomic |
|--|----------|--------|
| Семантика | До N параллельных | Ровно 1 |
| Ключ | Имя пула | Global или `AtomicOn(key)` |

Если для того же `name` создать Bulkhead с другим `max`, побеждает **первый** созданный gate (MVP).

---

## Atomic / AtomicOn (`Order 3`)

**Зачем.** Критическая секция in-process.

```csharp
await work.Useful(Intent.Atomic);           // один глобальный лок
await work.Useful(Intent.AtomicOn("order:1"));

await Intent.Atomically(() => { /* ... */ });
await Intent.Atomically("wallet:9", () => Debit(9));
```

**Поведение.** `SemaphoreSlim(1,1)` — глобальный или per-key. Ближе всего к user code: лок не держится на время backoff Retry и ожидания Timeout снаружи.

**Важно.** Не распределённый лок и не транзакция БД. Разные ключи не блокируют друг друга; один ключ — взаимное исключение.

---

## Что не политика, но рядом

| API | Роль |
|-----|------|
| `Intent.WhenAll` | Параллельный запуск нескольких Intent |
| `Intent.Sequence` | Последовательный запуск |
| `Background()` | Schedule без await; faults → `IntentDiagnostics` |
| `Then` / `Select` | Цепочка по результату `Intent<T>` |
| `IntentProfile.Http` / `DbWrite` | Готовые packs для `Useful(...)` |

Политики на композите оборачивают **весь** агрегат, не каждого ребёнка. Ретрай/timeout на каждый child — вешайте `Useful` на сами `a`, `b`.

---

## Как выбрать набор

```
Нужна отмена извне?           → Cancel + Run(async ct => …)
Нужны метрики/трасса?         → Named + Tag + Trace/Activity/Metrics
Чтение без side effects?      → Cache
Side effect + Retry?          → Idempotent(key)
Внешний сервис?               → IntentProfile.Http / CB+Bulkhead+Retry+Timeout
Критическая секция / DB write?→ IntentProfile.DbWrite / AtomicOn
Общий бюджет времени?         → Timeout
Лимит на одну попытку?        → Retry(..., attemptTimeout:)
```

Типичный I/O-профиль:

```csharp
.Useful(IntentProfile.Http("Fetch"), Intent.Cancel(ct))

// или вручную:
.Useful(
    Intent.Named("Fetch"),
    Intent.Activity,
    Intent.Metrics,
    Intent.CircuitBreaker("api"),
    Intent.Bulkhead("api", 16),
    Intent.Retry(3, IntentBackoff.Exponential(100.Milliseconds()), attemptTimeout: 1.Seconds()),
    Intent.Timeout(5.Seconds()),
    Intent.Cancel(ct)
)
```
