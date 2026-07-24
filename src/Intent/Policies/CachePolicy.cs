using System.Collections.Concurrent;

namespace Intents;

/// <summary>
/// In-memory result cache for <see cref="Intent{T}"/>. On <see cref="Intent"/> (no result) this is a no-op pass-through.
/// </summary>
public sealed class CachePolicy : IntentPolicy
{
    public CachePolicy(string key, TimeSpan ttl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (ttl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ttl), "TTL must be positive.");
        Key = key;
        Ttl = ttl;
    }

    public string Key { get; }
    public TimeSpan Ttl { get; }
    public int Order => -3;

    public Func<CancellationToken, Task> Wrap(Func<CancellationToken, Task> next) => next;
}

internal static class IntentCacheStore
{
    private sealed class Entry
    {
        public required object Value { get; init; }
        public required DateTimeOffset ExpiresAt { get; init; }
    }

    private static readonly ConcurrentDictionary<string, Entry> Store = new();

    public static bool TryGet<T>(string key, out T value)
    {
        if (Store.TryGetValue(key, out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow)
        {
            value = (T)entry.Value;
            return true;
        }

        Store.TryRemove(key, out _);
        value = default!;
        return false;
    }

    public static void Set<T>(string key, T value, TimeSpan ttl)
    {
        Store[key] = new Entry
        {
            Value = value!,
            ExpiresAt = DateTimeOffset.UtcNow.Add(ttl)
        };
    }

    public static void Clear() => Store.Clear();
}
