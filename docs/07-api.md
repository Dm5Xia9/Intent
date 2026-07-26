# Справочник API

Namespace: `Intents`.

## Фабрики и композиция

```csharp
Intent.Run(...) / Intent.Defer(...)
Intent.Atomically(...) / Intent.Atomically(key, ...)

Intent.WhenAll(params Intent[])
Intent.Sequence(params Intent[])
Intent.Background(intent)   // или intent.Background()

intent.Then(x => Next(x))           // Intent<T> → Intent<TResult>
intent.Then(async (x, ct) => ...)   // async bind
intent.Select(x => map(x))          // map результата
```

## Политики

```csharp
Intent.Cancel(CancellationToken)                 // -20
Intent.Named(string)                             // -15
Intent.Tag(key, value) / Tags((k,v), ...)        // -14
Intent.Trace                                     // -10
Intent.Activity                                  // -8  (ActivitySource "Intents.Intent")
Intent.Metrics                                   // -5
Intent.Idempotent(key, ttl?)                     // -4  (default TTL 1h)
Intent.Cache(key, ttl)                           // -3
Intent.CircuitBreaker(name, threshold=5, break?) // -2
Intent.Timeout(TimeSpan)                         // 0
Intent.Retry(attempts, backoff?, shouldRetry?, attemptTimeout?) // 1
Intent.Bulkhead(name, maxParallelism)            // 2
Intent.Atomic / Intent.AtomicOn(key)             // 3
```

Пресеты: `IntentProfile.Http(...)` / `IntentProfile.DbWrite(...)`.

Backoff: `IntentBackoff.Constant` / `Exponential`.

Подробное описание каждой политики: **[08-policies.md](08-policies.md)**.

## Примеры

```csharp
await Intent.Run(async ct => await client.GetAsync(url, ct))
    .Configure(IntentProfile.Http("Fetch"), Intent.Cancel(ct));

await Intent.Run(() => Charge(cmd))
    .Configure(
        Intent.Idempotent($"pay:{cmd.Key}"),
        Intent.Named("Charge"),
        Intent.Tag("userId", cmd.UserId),
        Intent.Retry(3));

await Intent.WhenAll(LoadA(), LoadB());
LoadC().Configure(Intent.Retry(2)).Background();
```

## Ограничения

- Circuit/Bulkhead/Atomic/Cache/Idempotent — process-local.
- Timeout не abort’ит код без cooperative cancel.
- WhenAll/Sequence не прокидывают child-политики автоматически.
