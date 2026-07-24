# Intent — модель отложенного выполнения

`Intent` отделяет **что выполнить** от **как выполнить**. Вызов метода возвращает «холодный» план; код стартует только после `Useful` и `await`.

**Начни отсюда:** [docs/00-buy-me.md](docs/00-buy-me.md) — зачем это нужно и какие киллер-фичи ты получаешь.  
Полная документация: **[docs/](docs/README.md)**.

## Быстрый старт

```csharp
await ProcessOrder()
    .Useful(
        Intent.Named("Checkout"),
        Intent.Tag("orderId", orderId),
        Intent.Activity,
        Intent.Metrics,
        Intent.Idempotent($"order:{orderId}"),
        Intent.CircuitBreaker("checkout"),
        Intent.Bulkhead("io", maxParallelism: 8),
        Intent.AtomicOn("order"),
        Intent.Retry(3, IntentBackoff.Exponential(100.Milliseconds()), attemptTimeout: 2.Seconds()),
        Intent.Timeout(10.Seconds()),
        Intent.Cancel(ct)
    );

// или готовый профиль:
await CallHttp().Useful(IntentProfile.Http("payments"));
```

## Политики

| Политика | Назначение |
|----------|------------|
| `Cancel` / `Named` / `Tag` / `Trace` / `Activity` / `Metrics` | Cancel + baggage + наблюдаемость |
| `Idempotent(key)` | Дедуп + in-flight join |
| `Cache(key, ttl)` | Кеш для `Intent<T>` |
| `CircuitBreaker(name)` | Fail-fast после серии ошибок |
| `Timeout` / `Retry(..., attemptTimeout:)` | Бюджет и ретраи |
| `Bulkhead(name, max)` | Лимит параллелизма |
| `Atomic` / `AtomicOn(key)` | Mutual exclusion |

Pipeline:

```
Cancel → Named → Tag → Trace → Activity → Metrics → Idempotent → Cache
  → CircuitBreaker → Timeout → Retry → Bulkhead → Atomic → код
```

## Композиция

```csharp
await Intent.WhenAll(a, b, c);
await Intent.Sequence(a, b, c);
Intent.Run(Work).Useful(Intent.Retry(3)).Background();

var total = await Intent.Run(() => Load(id))
    .Then(x => Intent.Run(() => Enrich(x)))
    .Select(x => x.Total);
```

```bash
dotnet test
dotnet run --project examples/Intent.Examples
```
