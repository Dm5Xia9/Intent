# Intent — модель отложенного выполнения

`Intent` — это намерение выполнить работу: вызов метода создаёт холодный план, а не запускает код. План описывает **что** сделать; через `With*` / `Configure` к нему цепляют политики — **как** выполнять. Реальное выполнение начинается на `await`. Один и тот же метод можно запускать в разных режимах, не меняя его тело.

**Resilience** (retry, timeout, circuit breaker, bulkhead) — пакет **[Intent.Polly](src/Intent.Polly)** поверх Polly v8.

Полная документация: **[docs/](docs/README.md)**.

## Быстрый старт

```csharp
using Intents;
using Intents.Polly;
using Intents.Time;

async Intent ProcessOrder(string orderId)
{
    Validate(orderId);
    await SaveAsync(orderId);
    await NotifyAsync(orderId);
}

// холодный план — тело ещё не выполняется
var plan = ProcessOrder("42")
    .WithNamed("Checkout")
    .WithRetry(3)
    .WithTimeout(10.Seconds());

await plan; // только здесь стартует pipeline + тело
```

TimeSpan helpers (`10.Seconds()`) — opt-in: `using Intents.Time;`.

Roslyn analyzers ship with the Intent package (`INT0001`–`INT0003`): discarded Intent without `Background()`, configure-after-await, un-awaited Intent.

> **Внимание.** `Intent` опирается на механизм `Task` (awaiter, `TaskCompletionSource`, thread pool), но сам **не является** `Task`: его нельзя передать туда, где ждут `Task`/`Task<T>`, и вызов метода с `async Intent` не стартует работу — в отличие от `async Task`.

## Политики

### Core (`Intent`)

| Политика | Назначение |
|----------|------------|
| `Cancel` / `Named` / `Tag` / `Trace` / `Activity` / `Metrics` | Cancel + baggage + OTel (ActivitySource/Meter `Intents.Intent`) |
| `Idempotent(key)` | Дедуп + in-flight join |
| `Cache(key, ttl)` | Кеш для `Intent<T>` |
| `Atomic` / `AtomicOn(key)` | Mutual exclusion |
| `Before` / `After` | Хуки на каждую попытку тела |

### Resilience (`Intent.Polly`)

| Политика | Назначение |
|----------|------------|
| `WithRetry` / `WithTimeout` | Бюджет и ретраи (Polly) |
| `WithCircuitBreaker(name)` | Fail-fast после серии ошибок |
| `WithBulkhead(name, max)` | Лимит параллелизма (Polly RateLimiter) |
| `WithResilience(pipeline)` | Готовый `ResiliencePipeline` |

Фабрики core: `IntentPolicies.*`. Resilience: `IntentPolly.*` / `using Intents.Polly`. Пакеты: `Configure` / `WithPolicies(IntentPolly.Http(...))`.

Pipeline:

```
Cancel → Named → Tag → Trace → Activity → Metrics → Idempotent → Cache
  → CircuitBreaker → Timeout → Retry → Bulkhead → Atomic → Before → After → код
```

## Композиция

```csharp
await Intent.WhenAll(a, b, c);
await Intent.Sequence(a, b, c);
Intent.From(Work).WithRetry(3).Background();
Load(id).WithRetry(2).Into(channel);          // Intent → channel
channel.FromEach(x => Handle(x), ct, into: next); // channel → Intent (→ next)

var total = await Intent.From(() => Load(id))
    .Then(x => Intent.From(() => Enrich(x)))
    .Select(x => x.Total);
```

```bash
dotnet test
dotnet run --project examples/Intent.Examples
dotnet run --project examples/Intent.ChannelPipeline
```

## NuGet

Пакеты: **Intent**, **Intent.Polly**. Публикация идёт из GitHub Actions по тегу `v*.*.*`.

```bash
git tag v0.3.0
git push origin v0.3.0
```

Workflow `.github/workflows/publish.yml` прогоняет тесты, пакует оба проекта, пушит на nuget.org и создаёт GitHub Release.

**Авторизация (один из вариантов):**

1. **Trusted Publishing (предпочтительно):** на [nuget.org → Trusted Publishing](https://www.nuget.org/account/trusted-publishing) добавь policy: owner/repo, workflow file `publish.yml`. В GitHub Secrets — `NUGET_USER` (username на nuget.org, не email).
2. **API key:** секрет `NUGET_API_KEY` с ключом с nuget.org.

CI на каждый push/PR: `.github/workflows/ci.yml`.
