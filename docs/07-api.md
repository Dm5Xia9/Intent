# Справочник API

Namespace: `Intents`.

## Фабрики и композиция

```csharp
Intent.From(...) / Intent.FromFactory(...)
Intent.Atomically(...) / Intent.Atomically(key, ...)

Intent.WhenAll(params Intent[])
Intent.Sequence(params Intent[])
Intent.Background(intent)   // или intent.Background()

intent.Then(x => Next(x))           // Intent<T> → Intent<TResult>
intent.Then(async (x, ct) => ...)   // async bind
intent.Select(x => map(x))          // map результата
```

## Политики

Фабрики: `IntentPolicies.*`. Fluent на плане: `With*`.

```csharp
// Fluent (предпочтительно)
work.WithNamed("Checkout").WithRetry(3).WithTimeout(10.Seconds());

// Фабрики + Configure / WithPolicies (пакеты, кастом)
work.Configure(IntentPolicies.Retry(3), IntentPolicies.Timeout(5.Seconds()));
work.WithPolicies(IntentProfile.Http("Fetch"), IntentPolicies.Cancel(ct));
work.WithPolicies(new MyPolicy());
```

```csharp
IntentPolicies.Cancel(CancellationToken)                 // -20  → WithCancel
IntentPolicies.Named(string)                             // -15  → WithNamed
IntentPolicies.Tag / Tags                                // -14  → WithTag / WithTags
IntentPolicies.Trace                                     // -10  → WithTrace
IntentPolicies.Activity                                  // -8   → WithActivity
IntentPolicies.Metrics                                   // -5   → WithMetrics
IntentPolicies.Idempotent(key, ttl?)                     // -4   → WithIdempotent
IntentPolicies.Cache(key, ttl)                           // -3   → WithCache
IntentPolicies.CircuitBreaker(name, …)                   // -2   → WithCircuitBreaker
IntentPolicies.Timeout(TimeSpan)                         // 0    → WithTimeout
IntentPolicies.Retry(…)                                  // 1    → WithRetry
IntentPolicies.Bulkhead(name, max)                       // 2    → WithBulkhead
IntentPolicies.Atomic / AtomicOn(key)                    // 3    → WithAtomic / WithAtomicOn
IntentPolicies.Before(...)                               // 4    → WithBefore
IntentPolicies.After(...)                                // 5    → WithAfter
```

Пресеты: `IntentProfile.Http(...)` / `IntentProfile.DbWrite(...)`.

Backoff: `IntentBackoff.Constant` / `Exponential`.

Подробное описание каждой политики: **[08-policies.md](08-policies.md)**.

## Примеры

```csharp
await Intent.From(async ct => await client.GetAsync(url, ct))
    .WithPolicies(IntentProfile.Http("Fetch"))
    .WithCancel(ct);

await Intent.From(() => Charge(cmd))
    .WithIdempotent($"pay:{cmd.Key}")
    .WithNamed("Charge")
    .WithTag("userId", cmd.UserId)
    .WithRetry(3);

await Intent.WhenAll(LoadA(), LoadB());
LoadC().WithRetry(2).Background();
```

## Ограничения

- Circuit/Bulkhead/Atomic/Cache/Idempotent — process-local.
- Timeout не abort’ит код без cooperative cancel.
- WhenAll/Sequence не прокидывают child-политики автоматически.
