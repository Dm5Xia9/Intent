namespace Intents;

/// <summary>
/// Fluent <c>With*</c> helpers that attach built-in policies to a cold Intent.
/// </summary>
public static class IntentWithExtensions
{
    public static Intent WithPolicies(this Intent intent, params IntentPolicy[] policies) =>
        intent.Configure(policies);

    public static Intent<T> WithPolicies<T>(this Intent<T> intent, params IntentPolicy[] policies) =>
        intent.Configure(policies);

    public static Intent WithCancel(this Intent intent, CancellationToken token) =>
        intent.Configure(IntentPolicies.Cancel(token));

    public static Intent<T> WithCancel<T>(this Intent<T> intent, CancellationToken token) =>
        intent.Configure(IntentPolicies.Cancel(token));

    public static Intent WithNamed(this Intent intent, string name) =>
        intent.Configure(IntentPolicies.Named(name));

    public static Intent<T> WithNamed<T>(this Intent<T> intent, string name) =>
        intent.Configure(IntentPolicies.Named(name));

    public static Intent WithTag(this Intent intent, string key, object? value) =>
        intent.Configure(IntentPolicies.Tag(key, value));

    public static Intent<T> WithTag<T>(this Intent<T> intent, string key, object? value) =>
        intent.Configure(IntentPolicies.Tag(key, value));

    public static Intent WithTags(this Intent intent, params (string Key, object? Value)[] tags) =>
        intent.Configure(IntentPolicies.Tags(tags));

    public static Intent<T> WithTags<T>(this Intent<T> intent, params (string Key, object? Value)[] tags) =>
        intent.Configure(IntentPolicies.Tags(tags));

    public static Intent WithTrace(this Intent intent) =>
        intent.Configure(IntentPolicies.Trace);

    public static Intent<T> WithTrace<T>(this Intent<T> intent) =>
        intent.Configure(IntentPolicies.Trace);

    public static Intent WithActivity(this Intent intent) =>
        intent.Configure(IntentPolicies.Activity);

    public static Intent<T> WithActivity<T>(this Intent<T> intent) =>
        intent.Configure(IntentPolicies.Activity);

    public static Intent WithMetrics(this Intent intent) =>
        intent.Configure(IntentPolicies.Metrics);

    public static Intent<T> WithMetrics<T>(this Intent<T> intent) =>
        intent.Configure(IntentPolicies.Metrics);

    public static Intent WithIdempotent(this Intent intent, string key, TimeSpan? ttl = null) =>
        intent.Configure(IntentPolicies.Idempotent(key, ttl));

    public static Intent<T> WithIdempotent<T>(this Intent<T> intent, string key, TimeSpan? ttl = null) =>
        intent.Configure(IntentPolicies.Idempotent(key, ttl));

    public static Intent WithCache(this Intent intent, string key, TimeSpan ttl) =>
        intent.Configure(IntentPolicies.Cache(key, ttl));

    public static Intent<T> WithCache<T>(this Intent<T> intent, string key, TimeSpan ttl) =>
        intent.Configure(IntentPolicies.Cache(key, ttl));

    public static Intent WithCircuitBreaker(
        this Intent intent,
        string name,
        int failureThreshold = 5,
        TimeSpan? breakDuration = null) =>
        intent.Configure(IntentPolicies.CircuitBreaker(name, failureThreshold, breakDuration));

    public static Intent<T> WithCircuitBreaker<T>(
        this Intent<T> intent,
        string name,
        int failureThreshold = 5,
        TimeSpan? breakDuration = null) =>
        intent.Configure(IntentPolicies.CircuitBreaker(name, failureThreshold, breakDuration));

    public static Intent WithTimeout(this Intent intent, TimeSpan timeout) =>
        intent.Configure(IntentPolicies.Timeout(timeout));

    public static Intent<T> WithTimeout<T>(this Intent<T> intent, TimeSpan timeout) =>
        intent.Configure(IntentPolicies.Timeout(timeout));

    public static Intent WithRetry(
        this Intent intent,
        int attempts,
        Func<int, TimeSpan>? backoff = null,
        Func<Exception, bool>? shouldRetry = null,
        TimeSpan? attemptTimeout = null) =>
        intent.Configure(IntentPolicies.Retry(attempts, backoff, shouldRetry, attemptTimeout));

    public static Intent<T> WithRetry<T>(
        this Intent<T> intent,
        int attempts,
        Func<int, TimeSpan>? backoff = null,
        Func<Exception, bool>? shouldRetry = null,
        TimeSpan? attemptTimeout = null) =>
        intent.Configure(IntentPolicies.Retry(attempts, backoff, shouldRetry, attemptTimeout));

    public static Intent WithBulkhead(this Intent intent, string name, int maxParallelism) =>
        intent.Configure(IntentPolicies.Bulkhead(name, maxParallelism));

    public static Intent<T> WithBulkhead<T>(this Intent<T> intent, string name, int maxParallelism) =>
        intent.Configure(IntentPolicies.Bulkhead(name, maxParallelism));

    public static Intent WithAtomic(this Intent intent) =>
        intent.Configure(IntentPolicies.Atomic);

    public static Intent<T> WithAtomic<T>(this Intent<T> intent) =>
        intent.Configure(IntentPolicies.Atomic);

    public static Intent WithAtomicOn(this Intent intent, string key) =>
        intent.Configure(IntentPolicies.AtomicOn(key));

    public static Intent<T> WithAtomicOn<T>(this Intent<T> intent, string key) =>
        intent.Configure(IntentPolicies.AtomicOn(key));

    public static Intent WithBefore(this Intent intent, Action action) =>
        intent.Configure(IntentPolicies.Before(action));

    public static Intent WithBefore(this Intent intent, Func<Task> action) =>
        intent.Configure(IntentPolicies.Before(action));

    public static Intent WithBefore(this Intent intent, Func<CancellationToken, Task> action) =>
        intent.Configure(IntentPolicies.Before(action));

    public static Intent<T> WithBefore<T>(this Intent<T> intent, Action action) =>
        intent.Configure(IntentPolicies.Before(action));

    public static Intent<T> WithBefore<T>(this Intent<T> intent, Func<Task> action) =>
        intent.Configure(IntentPolicies.Before(action));

    public static Intent<T> WithBefore<T>(this Intent<T> intent, Func<CancellationToken, Task> action) =>
        intent.Configure(IntentPolicies.Before(action));

    public static Intent WithAfter(this Intent intent, Action action) =>
        intent.Configure(IntentPolicies.After(action));

    public static Intent WithAfter(this Intent intent, Func<Task> action) =>
        intent.Configure(IntentPolicies.After(action));

    public static Intent WithAfter(this Intent intent, Func<CancellationToken, Task> action) =>
        intent.Configure(IntentPolicies.After(action));

    public static Intent<T> WithAfter<T>(this Intent<T> intent, Action action) =>
        intent.Configure(IntentPolicies.After(action));

    public static Intent<T> WithAfter<T>(this Intent<T> intent, Func<Task> action) =>
        intent.Configure(IntentPolicies.After(action));

    public static Intent<T> WithAfter<T>(this Intent<T> intent, Func<CancellationToken, Task> action) =>
        intent.Configure(IntentPolicies.After(action));
}
