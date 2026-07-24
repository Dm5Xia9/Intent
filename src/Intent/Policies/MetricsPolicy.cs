namespace Intents;

public sealed class MetricsPolicy : IntentPolicy
{
    public static MetricsPolicy Instance { get; } = new();

    private MetricsPolicy() { }

    public int Order => -5;

    public Func<CancellationToken, Task> Wrap(Func<CancellationToken, Task> next)
    {
        return async ct =>
        {
            var name = IntentAmbient.Name;
            var tags = SnapshotTags();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var success = false;
            try
            {
                await next(ct).ConfigureAwait(false);
                success = true;
            }
            finally
            {
                sw.Stop();
                IntentMetrics.Record(name, sw.Elapsed, success);
                IntentDiagnostics.EmitMetric(new IntentMetricEvent(name, sw.Elapsed, success, tags));
            }
        };
    }

    private static IReadOnlyDictionary<string, object?>? SnapshotTags()
    {
        var tags = IntentAmbient.Tags;
        return tags.Count == 0 ? null : new Dictionary<string, object?>(tags);
    }
}

public readonly record struct IntentMetricEvent(
    string Name,
    TimeSpan Duration,
    bool Success,
    IReadOnlyDictionary<string, object?>? Tags = null);

public static class IntentMetrics
{
    private sealed class Counters
    {
        public long Count;
        public long Successes;
        public long Failures;
        public long TotalMs;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Counters> Store = new();

    public static void Record(string name, TimeSpan duration, bool success)
    {
        var c = Store.GetOrAdd(name, _ => new Counters());
        Interlocked.Increment(ref c.Count);
        if (success)
            Interlocked.Increment(ref c.Successes);
        else
            Interlocked.Increment(ref c.Failures);
        Interlocked.Add(ref c.TotalMs, (long)duration.TotalMilliseconds);
    }

    public static long GetCount(string name) =>
        Store.TryGetValue(name, out var c) ? Volatile.Read(ref c.Count) : 0;

    public static long GetSuccesses(string name) =>
        Store.TryGetValue(name, out var c) ? Volatile.Read(ref c.Successes) : 0;

    public static long GetFailures(string name) =>
        Store.TryGetValue(name, out var c) ? Volatile.Read(ref c.Failures) : 0;

    public static double GetAverageMilliseconds(string name)
    {
        if (!Store.TryGetValue(name, out var c))
            return 0;
        var count = Volatile.Read(ref c.Count);
        if (count == 0)
            return 0;
        return Volatile.Read(ref c.TotalMs) / (double)count;
    }

    public static void Reset() => Store.Clear();
}

public static class IntentDiagnostics
{
    public static event Action<IntentTraceEvent>? Traced;
    public static event Action<IntentMetricEvent>? Measured;

    public static void Reset()
    {
        Traced = null;
        Measured = null;
    }

    internal static void EmitTrace(IntentTraceEvent e) => Traced?.Invoke(e);

    internal static void EmitMetric(IntentMetricEvent e) => Measured?.Invoke(e);
}
