using System.Collections.Concurrent;

namespace Intents;

/// <summary>
/// In-memory result cache for <see cref="Intent{T}"/>. On <see cref="Intent"/> (no result) this is a no-op pass-through.
/// Implemented entirely via <see cref="Wrap"/> (no runner special-case).
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
    public int Order => IntentPipelineOrder.Cache;

    public Func<CancellationToken, Task> Wrap(Func<CancellationToken, Task> next)
    {
        var key = Key;
        var ttl = Ttl;
        return async ct =>
        {
            var bridge = IntentResultAccess.Current;
            if (bridge is null)
            {
                await next(ct).ConfigureAwait(false);
                return;
            }

            if (IntentCacheStore.TryGet(key, bridge.ResultType, out var cached))
            {
                bridge.SetStoredResult(cached);
                return;
            }

            await next(ct).ConfigureAwait(false);
            if (bridge.HasResult)
                IntentCacheStore.Set(key, bridge.CaptureResultAfterBody()!, ttl);
        };
    }
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
        if (TryGet(key, typeof(T), out var boxed))
        {
            value = (T)boxed!;
            return true;
        }

        value = default!;
        return false;
    }

    public static bool TryGet(string key, Type type, out object? value)
    {
        if (Store.TryGetValue(key, out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow)
        {
            if (entry.Value is null || type.IsInstanceOfType(entry.Value) ||
                (type.IsValueType && entry.Value.GetType() == type))
            {
                value = entry.Value;
                return true;
            }
        }

        Store.TryRemove(key, out _);
        value = null;
        return false;
    }

    public static void Set<T>(string key, T value, TimeSpan ttl) =>
        Set(key, (object?)value!, ttl);

    public static void Set(string key, object value, TimeSpan ttl)
    {
        Store[key] = new Entry
        {
            Value = value,
            ExpiresAt = DateTimeOffset.UtcNow.Add(ttl)
        };
    }

    public static void Clear() => Store.Clear();
}
