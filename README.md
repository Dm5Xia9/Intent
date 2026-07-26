# Intent — модель отложенного выполнения

`Intent` — это намерение выполнить работу: вызов метода создаёт холодный план, а не запускает код. План описывает **что** сделать; через `With*` / `Configure` к нему цепляют политики — **как** выполнять (retry, timeout, идемпотентность, изоляция…). Реальное выполнение начинается на `await`. Один и тот же метод можно запускать в разных режимах, не меняя его тело.

Полная документация: **[docs/](docs/README.md)**.

## Быстрый старт

```csharp
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

> **Внимание.** `Intent` опирается на механизм `Task` (awaiter, `TaskCompletionSource`, thread pool), но сам **не является** `Task`: его нельзя передать туда, где ждут `Task`/`Task<T>`, и вызов метода с `async Intent` не стартует работу — в отличие от `async Task`.

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
| `Before` / `After` | Хуки на каждую попытку тела |

Фабрики: `IntentPolicies.*`. Fluent: `.WithRetry(3).WithTimeout(...)`. Пакеты: `Configure` / `WithPolicies(IntentProfile.Http(...))`.

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

var total = await Intent.From(() => Load(id))
    .Then(x => Intent.From(() => Enrich(x)))
    .Select(x => x.Total);
```

```bash
dotnet test
dotnet run --project examples/Intent.Examples
```

## NuGet

Пакет: **Intent**. Публикация идёт из GitHub Actions по тегу `v*.*.*`.

```bash
git tag v0.1.0
git push origin v0.1.0
```

Workflow `.github/workflows/publish.yml` прогоняет тесты, пакует `src/Intent`, пушит на nuget.org и создаёт GitHub Release.

**Авторизация (один из вариантов):**

1. **Trusted Publishing (предпочтительно):** на [nuget.org → Trusted Publishing](https://www.nuget.org/account/trusted-publishing) добавь policy: owner/repo, workflow file `publish.yml`. В GitHub Secrets — `NUGET_USER` (username на nuget.org, не email).
2. **API key:** секрет `NUGET_API_KEY` с ключом с nuget.org.

CI на каждый push/PR: `.github/workflows/ci.yml`.
