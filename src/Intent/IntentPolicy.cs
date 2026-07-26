namespace Intents;

/// <summary>
/// A capability that wraps the next stage of execution (passed to <see cref="Intent.Configure"/>).
/// </summary>
public interface IntentPolicy
{
    /// <summary>
    /// Lower values wrap outer (run first). Normalized pipeline order:
    /// Cancel → Named → Tag → Trace → Activity → Metrics → Idempotent → Cache → (Intent.Polly) → Atomic → Before → After → user code.
    /// Numeric values: <see cref="IntentPipelineOrder"/> (frozen; see CHANGELOG).
    /// </summary>
    int Order { get; }

    Func<CancellationToken, Task> Wrap(Func<CancellationToken, Task> next);
}
