using System.Collections.Concurrent;

namespace Intents;

/// <summary>
/// Deduplicates executions by key: concurrent callers share one run; successful results
/// are remembered for <see cref="Ttl"/>. Failures do not stick — a later call may retry.
/// </summary>
public sealed class IdempotentPolicy : IntentPolicy
{
    public IdempotentPolicy(string key, TimeSpan ttl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (ttl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ttl), "TTL must be positive.");
        Key = key;
        Ttl = ttl;
    }

    public string Key { get; }
    public TimeSpan Ttl { get; }

    /// <summary>Outside Cache so an idempotent hit never touches the inner stack.</summary>
    public int Order => -4;

    /// <summary>Logic lives in <see cref="Intent"/> / <see cref="Intent{T}"/> pipeline runners.</summary>
    public Func<CancellationToken, Task> Wrap(Func<CancellationToken, Task> next) => next;
}

internal static class IntentIdempotencyStore
{
    private abstract class Slot;

    private sealed class InFlight : Slot
    {
        public TaskCompletionSource<object?> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Completed : Slot
    {
        public required object? Value { get; init; }
        public required DateTimeOffset ExpiresAt { get; init; }
    }

    private static readonly ConcurrentDictionary<string, Slot> Store = new();
    private static readonly object VoidSentinel = new();

    public static async Task RunVoidAsync(
        string key,
        TimeSpan ttl,
        Func<CancellationToken, Task> next,
        CancellationToken ct)
    {
        await RunResultAsync<object?>(
            key,
            ttl,
            async token =>
            {
                await next(token).ConfigureAwait(false);
                return VoidSentinel;
            },
            ct).ConfigureAwait(false);
    }

    public static async Task<(bool FromStore, T Value)> RunResultAsync<T>(
        string key,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> next,
        CancellationToken ct)
    {
        while (true)
        {
            if (Store.TryGetValue(key, out var existing))
            {
                if (existing is Completed completed)
                {
                    if (completed.ExpiresAt > DateTimeOffset.UtcNow)
                        return (true, Unwrap<T>(completed.Value));

                    Store.TryRemove(key, out _);
                }
                else if (existing is InFlight inflight)
                {
                    var shared = await inflight.Completion.Task.ConfigureAwait(false);
                    return (true, Unwrap<T>(shared));
                }
            }

            var flight = new InFlight();
            if (!Store.TryAdd(key, flight))
                continue;

            try
            {
                var value = await next(ct).ConfigureAwait(false);
                var boxed = Box(value);
                Store[key] = new Completed
                {
                    Value = boxed,
                    ExpiresAt = DateTimeOffset.UtcNow.Add(ttl)
                };
                flight.Completion.TrySetResult(boxed);
                return (false, value);
            }
            catch (Exception ex)
            {
                Store.TryRemove(key, out _);
                flight.Completion.TrySetException(ex);
                throw;
            }
        }
    }

    private static object? Box<T>(T value) => value;

    private static T Unwrap<T>(object? boxed)
    {
        if (ReferenceEquals(boxed, VoidSentinel))
            return default!;
        return (T)boxed!;
    }

    public static void Clear() => Store.Clear();
}
