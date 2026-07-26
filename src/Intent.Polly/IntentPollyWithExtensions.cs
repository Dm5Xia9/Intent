using Polly;

namespace Intents.Polly;

/// <summary>
/// Fluent resilience helpers backed by Polly v8.
/// </summary>
public static class IntentPollyWithExtensions
{
    public static Intent WithResilience(this Intent intent, ResiliencePipeline pipeline, int order = 0) =>
        intent.Configure(IntentPolly.Resilience(pipeline, order));

    public static Intent<T> WithResilience<T>(this Intent<T> intent, ResiliencePipeline pipeline, int order = 0) =>
        intent.Configure(IntentPolly.Resilience(pipeline, order));

    public static Intent WithTimeout(this Intent intent, TimeSpan timeout) =>
        intent.Configure(IntentPolly.Timeout(timeout));

    public static Intent<T> WithTimeout<T>(this Intent<T> intent, TimeSpan timeout) =>
        intent.Configure(IntentPolly.Timeout(timeout));

    public static Intent WithRetry(
        this Intent intent,
        int attempts,
        Func<int, TimeSpan>? backoff = null,
        Func<Exception, bool>? shouldRetry = null,
        TimeSpan? attemptTimeout = null) =>
        intent.Configure(IntentPolly.Retry(attempts, backoff, shouldRetry, attemptTimeout));

    public static Intent<T> WithRetry<T>(
        this Intent<T> intent,
        int attempts,
        Func<int, TimeSpan>? backoff = null,
        Func<Exception, bool>? shouldRetry = null,
        TimeSpan? attemptTimeout = null) =>
        intent.Configure(IntentPolly.Retry(attempts, backoff, shouldRetry, attemptTimeout));

    public static Intent WithCircuitBreaker(
        this Intent intent,
        string name,
        int failureThreshold = 5,
        TimeSpan? breakDuration = null) =>
        intent.Configure(IntentPolly.CircuitBreaker(name, failureThreshold, breakDuration));

    public static Intent<T> WithCircuitBreaker<T>(
        this Intent<T> intent,
        string name,
        int failureThreshold = 5,
        TimeSpan? breakDuration = null) =>
        intent.Configure(IntentPolly.CircuitBreaker(name, failureThreshold, breakDuration));

    public static Intent WithBulkhead(this Intent intent, string name, int maxParallelism) =>
        intent.Configure(IntentPolly.Bulkhead(name, maxParallelism));

    public static Intent<T> WithBulkhead<T>(this Intent<T> intent, string name, int maxParallelism) =>
        intent.Configure(IntentPolly.Bulkhead(name, maxParallelism));
}
