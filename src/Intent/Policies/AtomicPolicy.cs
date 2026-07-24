using System.Collections.Concurrent;

namespace Intents;

public sealed class AtomicPolicy : IntentPolicy
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();
    internal static readonly SemaphoreSlim GlobalGate = new(1, 1);

    public static AtomicPolicy Instance { get; } = new(null);

    public static AtomicPolicy ForKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return new AtomicPolicy(key);
    }

    private AtomicPolicy(string? key) => Key = key;

    public string? Key { get; }
    public int Order => 3;

    public Func<CancellationToken, Task> Wrap(Func<CancellationToken, Task> next)
    {
        var gate = Key is null ? GlobalGate : Gates.GetOrAdd(Key, _ => new SemaphoreSlim(1, 1));
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
