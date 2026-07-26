# Справочник API

Namespace core: `Intents`. Resilience: `Intents.Polly`. Time helpers: `Intents.Time` (`using Intents.Time;` for `10.Seconds()`).

Pipeline order constants: `IntentPipelineOrder` (see CHANGELOG for stability rules).
Analyzers (with Intent package): `INT0001` discard without Background, `INT0002` configure-after-await, `INT0003` Intent not awaited.

## Фабрики и композиция

```csharp
Intent.From(...) / Intent.FromFactory(...)
Intent.Atomically(...) / Intent.Atomically(key, ...)

Intent.WhenAll(params Intent[])
Intent.Sequence(params Intent[])
Intent.Background(intent)   // или intent.Background()
intent.Into(channel)        // или Into(writer) — background + запись результата
channel.FromEach(x => Work(x), ct)  // background: Intent на каждый item (опц. into: nextWriter)

intent.Then(x => Next(x))           // Intent<T> → Intent<TResult>
intent.Then(async (x, ct) => ...)   // async bind
intent.Select(x => map(x))          // map результата
```

## Политики core

Фабрики: `IntentPolicies.*`. Fluent: `With*`.

```csharp
IntentPolicies.Cancel(CancellationToken)                 // -20  → WithCancel
IntentPolicies.Named(string)                             // -15  → WithNamed
IntentPolicies.Tag / Tags                                // -14  → WithTag / WithTags
IntentPolicies.Trace                                     // -10  → WithTrace
IntentPolicies.Activity                                  // -8   → WithActivity  (source Intents.Intent)
IntentPolicies.Metrics                                   // -5   → WithMetrics  (meter Intents.Intent)
IntentPolicies.Idempotent(key, ttl?)                     // -4   → WithIdempotent
IntentPolicies.Cache(key, ttl)                           // -3   → WithCache
IntentPolicies.Atomic / AtomicOn(key)                    // 3    → WithAtomic / WithAtomicOn
IntentPolicies.Before(...)                               // 4    → WithBefore
IntentPolicies.After(...)                                // 5    → WithAfter
```

## Resilience (`Intent.Polly`)

```csharp
using Intents.Polly;

work.WithNamed("Checkout").WithRetry(3).WithTimeout(10.Seconds());
work.WithResilience(myPipeline);
work.WithPolicies(IntentPolly.Http("Fetch"), IntentPolicies.Cancel(ct));
```

```csharp
IntentPolly.Resilience(pipeline, order?)                 // → WithResilience
IntentPolly.CircuitBreaker(name, …)                      // -2   → WithCircuitBreaker
IntentPolly.Timeout(TimeSpan)                            // 0    → WithTimeout
IntentPolly.Retry(…)                                     // 1    → WithRetry
IntentPolly.Bulkhead(name, max)                          // 2    → WithBulkhead
```

Пресеты: `IntentPolly.Http(...)` / `IntentPolly.DbWrite(...)`.

Backoff: `IntentBackoff.Constant` / `Exponential`.

Исключения Polly: `TimeoutRejectedException`, `BrokenCircuitException`, …

Подробнее: **[08-policies.md](08-policies.md)**.

## Примеры

```csharp
await Intent.From(async ct => await client.GetAsync(url, ct))
    .WithPolicies(IntentPolly.Http("Fetch"))
    .WithCancel(ct);

await Intent.From(() => Charge(cmd))
    .WithIdempotent($"pay:{cmd.Key}")
    .WithNamed("Charge")
    .WithTag("userId", cmd.UserId)
    .WithRetry(3);

await Intent.WhenAll(LoadA(), LoadB());
LoadC().WithRetry(2).Background();
LoadD().WithRetry(2).Into(channel);
channel.FromEach(x => Handle(x).WithRetry(2), ct, into: next);
```

## Ограничения

- Named CB/Bulkhead state — внутри Polly registries (process-local).
- Atomic/Cache/Idempotent — process-local.
- Timeout не abort’ит код без cooperative cancel (Polly Timeout strategy).
- WhenAll/Sequence не прокидывают child-политики автоматически.
