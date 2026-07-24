using System.Collections.Concurrent;

namespace Intents;

/// <summary>
/// Limits concurrent executions for a named pool (bulkhead).
/// </summary>
public sealed class BulkheadPolicy : IntentPolicy
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();

    public BulkheadPolicy(string name, int maxParallelism)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (maxParallelism < 1)
            throw new ArgumentOutOfRangeException(nameof(maxParallelism));

        Name = name;
        MaxParallelism = maxParallelism;
    }

    public string Name { get; }
    public int MaxParallelism { get; }
    public int Order => 2;

    public Func<CancellationToken, Task> Wrap(Func<CancellationToken, Task> next)
    {
        var gate = Gates.GetOrAdd(Name, _ => new SemaphoreSlim(MaxParallelism, MaxParallelism));
        // If key already existed with different max, keep existing gate (document this).
        return async ct =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await next(ct).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        };
    }
}
