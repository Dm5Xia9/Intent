# Кейсы и антипаттерны

Примеры с Retry/Timeout/CB предполагают `using Intents.Polly;`.
`N.Seconds()` / `N.Milliseconds()` — `using Intents.Time;`.

Предпочтительный способ навешивать политики — **builder API** (`With*`). `Configure(...)` остаётся низкоуровневым API для массива `IntentPolicy`.

## Где Intent помогает

### 1. Бизнес-операция с разными режимами запуска

```csharp
async Intent Checkout(Cart cart) { ... }

// UI: быстро упасть
await Checkout(cart);

// Worker: терпеливо + наблюдаемость
await Checkout(cart)
    .WithNamed("Checkout")
    .WithActivity()
    .WithRetry(5, IntentBackoff.Exponential(200.Milliseconds()), attemptTimeout: 3.Seconds())
    .WithTimeout(30.Seconds())
    .WithCancel(ct);
```

Один метод — несколько профилей исполнения. Для типового HTTP: `IntentPolly.Http("Checkout")`.

### 1b. Идемпотентный side effect

```csharp
await Intent.From(() => Charge(cmd))
    .WithIdempotent($"pay:{cmd.IdempotencyKey}")
    .WithRetry(3)
    .WithTimeout(10.Seconds());
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
await Transfer().WithAtomicOn("account:7");
```

Структуры остаются простыми; синхронизация — политика.

### 3. Cross-cutting снаружи тела

```csharp
await LoadProfile(userId)
    .WithNamed("LoadProfile")
    .WithTrace()
    .WithActivity()
    .WithCache($"profile:{userId}", 30.Seconds());
```

Имя, трасса, Activity, метрики и кеш навешиваются снаружи — тело метода не знает об этом.

### 4. Защита внешнего I/O

```csharp
await Intent.From(async ct => await http.GetAsync(url, ct))
    .WithNamed("HttpGet")
    .WithCircuitBreaker("payments-api")
    .WithBulkhead("http", maxParallelism: 32)
    .WithRetry(3, attemptTimeout: 1.Seconds())
    .WithTimeout(5.Seconds())
    .WithCancel(ct);
```

- **CircuitBreaker** — fail-fast, когда зависимость уже лежит.
- **Bulkhead** — не съесть весь thread pool одним типом вызовов.
- **attemptTimeout** — одна попытка не съедает весь бюджет `Timeout`.

### 5. Явная граница «ещё не началось»

```csharp
var batch = orders.Select(o => Process(o)).ToList();
if (dryRun) return;

await Intent.WhenAll(batch.ToArray())
    .WithBulkhead("orders", 8)
    .WithRetry(2);
```

С `Task` к моменту `Select` работа уже могла стартовать.

### 6. Композиция планов

```csharp
await Intent.Sequence(Validate(), Save(), Notify());

await Intent.WhenAll(WarmCacheA(), WarmCacheB());

// Явный background с отчётом об ошибке в IntentDiagnostics
RefreshAsync().WithRetry(2).Background();
```

### 6b. Фон → канал (`Into`) и канал → Intent (`FromEach`)

`Into` — положить результат Intent в канал. `FromEach` — наоборот: в фоне читать канал и на каждый элемент строить/await’ить Intent.

```csharp
using System.Threading.Channels;

var results = Channel.CreateUnbounded<OrderDto>();
var done = Channel.CreateUnbounded<Receipt>();

// producer
FetchOrder(id).WithRetry(2).Into(results);

// consumer: каждый item → Intent (опционально сразу в следующий канал)
results.FromEach(
    order => Process(order).WithRetry(2),
    ct,
    into: done.Writer,
    onCompleted: () => Console.WriteLine("drain done"));
```

`Into`: schedule без await; ошибка → `IntentDiagnostics`; канал **не** закрывается (`Complete`).  
`FromEach`: обязательный `CancellationToken`; ошибка на одном item → diagnostics, цикл **продолжается**; при завершении reader’а вызывается `onCompleted`, а `into` (если задан) закрывается через `Complete`.

Полный runnable пример: **[examples/Intent.ChannelPipeline](../examples/Intent.ChannelPipeline)**.

```csharp
foreach (var id in ids)
    Load(id).WithTimeout(5.Seconds()).Into(inbox);

inbox.FromEach(item => Handle(item).WithNamed("handle"), ct);
```

### 7. Тесты политик отдельно от домена

Доменный метод пишет «что». Политики проверяются на `Intent.From` без подъёма всего сценария:

```csharp
await Intent.From(Flaky)
    .WithRetry(3, shouldRetry: ex => ex is HttpRequestException);
```

## Где Intent не нужен

- Простой I/O в ASP.NET без политик: `return await db.SaveAsync()`.
- Библиотека с контрактом `Task`/`ValueTask` для всех потребителей.
- Микрооптимизации tight loop.
- Нужен только resilience без cold plan — используйте Polly напрямую; Intent + Intent.Polly — когда нужен cold Intent поверх того же Polly.

## Антипаттерны

### Навесить With* / Configure после старта

```csharp
var i = Work();
await i;
i.WithRetry(3); // бросит
```

### Думать, что Atomic / AtomicOn / Bulkhead / Circuit / Cache — распределённые

Все process-local. Два сервера их не разделяют.

### Думать, что Timeout убивает поток

Timeout (и `attemptTimeout`) ограничивают **ожидание**. Код без cooperative cancel / без `CancellationToken` может доработать в фоне. Для отмены тела используйте `Intent.From(async ct => ...)` + `Cancel`/`Timeout`.

### Забытый Intent без Background / Into / FromEach

```csharp
_ = ProcessOrder(); // план создан — работа не началась
ProcessOrder().Background(); // schedule + ошибки в diagnostics
Load(id).Into(channel);      // schedule + результат в канал
channel.FromEach(x => Handle(x), ct); // schedule consumer
```

### Политики на WhenAll ≠ политики на детях

```csharp
await Intent.WhenAll(a, b).WithRetry(3);
// Retry оборачивает весь WhenAll, а не каждый из a/b отдельно
```

Если нужен ретрай на каждый child — вешайте `WithRetry` на `a` и `b`.

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
  → .Background()

Фон + результат в Channel?
  → Intent<T>.Into(channel)

Канал → Intent на каждый item?
  → channel.FromEach(x => Handle(x), ct)
```
