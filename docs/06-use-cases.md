# Кейсы и антипаттерны

## Где Intent помогает

### 1. Бизнес-операция с разными режимами запуска

```csharp
async Intent Checkout(Cart cart) { ... }

// UI: быстро упасть
await Checkout(cart);

// Worker: терпеливо + наблюдаемость
await Checkout(cart)
    .Useful(
        Intent.Named("Checkout"),
        Intent.Activity,
        Intent.Metrics,
        Intent.Retry(5, IntentBackoff.Exponential(200.Milliseconds()), attemptTimeout: 3.Seconds()),
        Intent.Timeout(30.Seconds()),
        Intent.Cancel(ct)
    );
```

Один метод — несколько профилей исполнения. Для типового HTTP: `IntentProfile.Http("Checkout")`.

### 1b. Идемпотентный side effect

```csharp
await Intent.Run(() => Charge(cmd))
    .Useful(
        Intent.Idempotent($"pay:{cmd.IdempotencyKey}"),
        Intent.Retry(3),
        Intent.Timeout(10.Seconds()));
```

Повтор с тем же ключом не спишет дважды; параллельные запросы делят один run.

### 2. Атомарность снаружи мелких команд (CQS)

```csharp
await Intent.Atomically(() =>
{
    var v = stack.Peek();
    stack.Pop();
});

// Разные ресурсы — разные ключи
await Intent.Atomically("wallet:42", () => Debit(42));
await Transfer().Useful(Intent.AtomicOn("account:7"));
```

Структуры остаются простыми; синхронизация — политика.

### 3. Cross-cutting без builder-API

```csharp
await LoadProfile(userId)
    .Useful(
        Intent.Named("LoadProfile"),
        Intent.Trace,
        Intent.Activity,
        Intent.Metrics,
        Intent.Cache($"profile:{userId}", 30.Seconds())
    );
```

Имя, трасса, Activity, метрики и кеш навешиваются снаружи — тело метода не знает об этом.

### 4. Защита внешнего I/O

```csharp
await Intent.Run(async ct => await http.GetAsync(url, ct))
    .Useful(
        Intent.Named("HttpGet"),
        Intent.CircuitBreaker("payments-api"),
        Intent.Bulkhead("http", maxParallelism: 32),
        Intent.Retry(3, attemptTimeout: 1.Seconds()),
        Intent.Timeout(5.Seconds()),
        Intent.Cancel(ct)
    );
```

- **CircuitBreaker** — fail-fast, когда зависимость уже лежит.
- **Bulkhead** — не съесть весь thread pool одним типом вызовов.
- **attemptTimeout** — одна попытка не съедает весь бюджет `Timeout`.

### 5. Явная граница «ещё не началось»

```csharp
var batch = orders.Select(o => Process(o)).ToList();
if (dryRun) return;

await Intent.WhenAll(batch.ToArray())
    .Useful(Intent.Bulkhead("orders", 8), Intent.Retry(2));
```

С `Task` к моменту `Select` работа уже могла стартовать.

### 6. Композиция планов

```csharp
await Intent.Sequence(Validate(), Save(), Notify());

await Intent.WhenAll(WarmCacheA(), WarmCacheB());

// Явный background с отчётом об ошибке в IntentDiagnostics
RefreshAsync().Useful(Intent.Retry(2)).Background();
```

### 7. Тесты политик отдельно от домена

Доменный метод пишет «что». Политики проверяются на `Intent.Run` без подъёма всего сценария:

```csharp
await Intent.Run(Flaky)
    .Useful(Intent.Retry(3, shouldRetry: ex => ex is HttpRequestException));
```

## Где Intent не нужен

- Простой I/O в ASP.NET без политик: `return await db.SaveAsync()`.
- Библиотека с контрактом `Task`/`ValueTask` для всех потребителей.
- Микрооптимизации tight loop.
- Уже есть Polly/workflow engine и команда ими живёт — не плодить вторую абстракцию без нужды.

## Антипаттерны

### Навесить Useful после старта

```csharp
var i = Work();
await i;
i.Useful(Intent.Retry(3)); // бросит
```

### Думать, что Atomic / AtomicOn / Bulkhead / Circuit / Cache — распределённые

Все process-local. Два сервера их не разделяют.

### Думать, что Timeout убивает поток

Timeout (и `attemptTimeout`) ограничивают **ожидание**. Код без cooperative cancel / без `CancellationToken` может доработать в фоне. Для отмены тела используйте `Intent.Run(async ct => ...)` + `Cancel`/`Timeout`.

### Забытый Intent без Background

```csharp
_ = ProcessOrder(); // план создан — работа не началась
ProcessOrder().Background(); // так: schedule + ошибки в diagnostics
```

### Политики на WhenAll ≠ политики на детях

```csharp
await Intent.WhenAll(a, b).Useful(Intent.Retry(3));
// Retry оборачивает весь WhenAll, а не каждый из a/b отдельно
```

Если нужен ретрай на каждый child — вешайте `Useful` на `a` и `b`.

### Заменить все Task на Intent «для красоты»

Intent — для **операций с политиками**, не для каждого helper’а.

## Шпаргалка выбора

```
Нужны политики / отложенный старт / разные профили запуска?
  да → Intent
  нет → Task / ValueTask

Ретрай / таймаут на попытку?
  → Retry(n, attemptTimeout: ...)
  общий бюджет → + Timeout(...)

Внешний сервис нестабилен?
  → CircuitBreaker + Bulkhead + Cancel(ct)

Критическая секция?
  → Atomic / AtomicOn(key) / Atomically(...)

Кеш чтения?
  → Intent<T> + Cache(key, ttl)

Параллель / цепочка планов?
  → WhenAll / Sequence

Fire-and-forget?
  → .Background()   (не просто _)
```
