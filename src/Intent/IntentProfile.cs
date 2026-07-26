namespace Intents;

/// <summary>
/// Ready-made policy packs for common scenarios. Pass to <see cref="Intent.Configure"/> or <c>WithPolicies</c>.
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
        IntentPolicies.Named(name),
        IntentPolicies.Activity,
        IntentPolicies.Metrics,
        IntentPolicies.CircuitBreaker(name, failureThreshold, breakDuration),
        IntentPolicies.Bulkhead(name, maxParallelism),
        IntentPolicies.Retry(
            retryAttempts,
            IntentBackoff.Exponential(100.Milliseconds()),
            attemptTimeout: attemptTimeout ?? 2.Seconds()),
        IntentPolicies.Timeout(timeout ?? 10.Seconds())
    ];

    /// <summary>
    /// Named + Activity + AtomicOn + Retry (skips cancellation by default filter of Retry).
    /// </summary>
    public static IntentPolicy[] DbWrite(
        string name = "db-write",
        string? atomicKey = null,
        int retryAttempts = 3) =>
    [
        IntentPolicies.Named(name),
        IntentPolicies.Activity,
        IntentPolicies.AtomicOn(atomicKey ?? name),
        IntentPolicies.Retry(retryAttempts)
    ];
}
