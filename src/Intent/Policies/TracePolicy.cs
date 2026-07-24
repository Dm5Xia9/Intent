namespace Intents;

public sealed class TracePolicy : IntentPolicy
{
    public static TracePolicy Instance { get; } = new();

    private TracePolicy() { }

    public int Order => -10;

    public Func<CancellationToken, Task> Wrap(Func<CancellationToken, Task> next)
    {
        return async ct =>
        {
            var name = IntentAmbient.Name;
            var tags = SnapshotTags();
            var started = DateTimeOffset.UtcNow;
            IntentDiagnostics.EmitTrace(new IntentTraceEvent(name, IntentTracePhase.Started, null, null, tags));
            try
            {
                await next(ct).ConfigureAwait(false);
                IntentDiagnostics.EmitTrace(new IntentTraceEvent(
                    name, IntentTracePhase.Succeeded, DateTimeOffset.UtcNow - started, null, tags));
            }
            catch (Exception ex)
            {
                IntentDiagnostics.EmitTrace(new IntentTraceEvent(
                    name, IntentTracePhase.Faulted, DateTimeOffset.UtcNow - started, ex, tags));
                throw;
            }
        };
    }

    private static IReadOnlyDictionary<string, object?>? SnapshotTags()
    {
        var tags = IntentAmbient.Tags;
        return tags.Count == 0 ? null : new Dictionary<string, object?>(tags);
    }
}

public enum IntentTracePhase
{
    Started,
    Succeeded,
    Faulted
}

public readonly record struct IntentTraceEvent(
    string Name,
    IntentTracePhase Phase,
    TimeSpan? Duration,
    Exception? Exception,
    IReadOnlyDictionary<string, object?>? Tags = null);
