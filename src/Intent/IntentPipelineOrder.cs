namespace Intents;

/// <summary>
/// Frozen policy <see cref="IntentPolicy.Order"/> values (outer → inner as numbers increase).
/// Changing these is a breaking change — see CHANGELOG (allowed only before 1.0).
/// </summary>
public static class IntentPipelineOrder
{
    public const int Cancel = -20;
    public const int Named = -15;
    public const int Tag = -14;
    public const int Trace = -10;
    public const int Activity = -8;
    public const int Metrics = -5;
    public const int Idempotent = -4;
    public const int Cache = -3;
    public const int CircuitBreaker = -2;
    public const int Timeout = 0;
    public const int Retry = 1;
    public const int Bulkhead = 2;
    public const int Atomic = 3;
    public const int Before = 4;
    public const int After = 5;
}
