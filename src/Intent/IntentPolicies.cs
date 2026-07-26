namespace Intents;

/// <summary>
/// Factories for built-in <see cref="IntentPolicy"/> instances. Prefer <c>With*</c> on an Intent for fluent configuration.
/// Resilience (retry, timeout, circuit breaker, bulkhead) lives in the <c>Intent.Polly</c> package.
/// </summary>
public static class IntentPolicies
{
    public static IntentPolicy Atomic { get; } = AtomicPolicy.Instance;

    public static IntentPolicy AtomicOn(string key) => AtomicPolicy.ForKey(key);

    public static IntentPolicy Cancel(CancellationToken token) => new CancelPolicy(token);

    public static IntentPolicy Named(string name) => new NamedPolicy(name);

    public static IntentPolicy Tag(string key, object? value) =>
        new TagPolicy([new KeyValuePair<string, object?>(key, value)]);

    public static IntentPolicy Tags(params (string Key, object? Value)[] tags) =>
        new TagPolicy(tags.Select(t => new KeyValuePair<string, object?>(t.Key, t.Value)));

    public static IntentPolicy Trace { get; } = TracePolicy.Instance;

    public static IntentPolicy Metrics { get; } = MetricsPolicy.Instance;

    public static IntentPolicy Activity { get; } = ActivityPolicy.Instance;

    public static IntentPolicy Idempotent(string key, TimeSpan? ttl = null) =>
        new IdempotentPolicy(key, ttl ?? TimeSpan.FromHours(1));

    public static IntentPolicy Cache(string key, TimeSpan ttl) => new CachePolicy(key, ttl);

    public static IntentPolicy Before(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new BeforePolicy(_ =>
        {
            action();
            return Task.CompletedTask;
        });
    }

    public static IntentPolicy Before(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new BeforePolicy(async _ => await action().ConfigureAwait(false));
    }

    public static IntentPolicy Before(Func<CancellationToken, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new BeforePolicy(action);
    }

    public static IntentPolicy After(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new AfterPolicy(_ =>
        {
            action();
            return Task.CompletedTask;
        });
    }

    public static IntentPolicy After(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new AfterPolicy(async _ => await action().ConfigureAwait(false));
    }

    public static IntentPolicy After(Func<CancellationToken, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new AfterPolicy(action);
    }
}
