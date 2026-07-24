namespace Intents;

/// <summary>
/// Ready-made policy packs for common scenarios. Pass to <see cref="Intent.Useful"/>.
/// </summary>
public static class IntentProfile
{
    /// <summary>
    /// Named + Activity + Metrics + CircuitBreaker + Bulkhead + Retry(+attemptTimeout) + Timeout.
    /// </summary>
    public static IntentPolicy[] Http(
        string name = "http",
        int retryAttempts = 3,
        TimeSpan? timeout = null,
        TimeSpan? attemptTimeout = null,
        int failureThreshold = 5,
        TimeSpan? breakDuration = null,
        int maxParallelism = 32) =>
    [
        Intent.Named(name),
        Intent.Activity,
        Intent.Metrics,
        Intent.CircuitBreaker(name, failureThreshold, breakDuration),
        Intent.Bulkhead(name, maxParallelism),
        Intent.Retry(
            retryAttempts,
            IntentBackoff.Exponential(100.Milliseconds()),
            attemptTimeout: attemptTimeout ?? 2.Seconds()),
        Intent.Timeout(timeout ?? 10.Seconds())
    ];

    /// <summary>
    /// Named + Activity + AtomicOn + Retry (skips cancellation by default filter of Retry).
    /// </summary>
    public static IntentPolicy[] DbWrite(
        string name = "db-write",
        string? atomicKey = null,
        int retryAttempts = 3) =>
    [
        Intent.Named(name),
        Intent.Activity,
        Intent.AtomicOn(atomicKey ?? name),
        Intent.Retry(retryAttempts)
    ];
}
