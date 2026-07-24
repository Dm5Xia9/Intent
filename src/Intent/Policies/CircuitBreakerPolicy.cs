using System.Collections.Concurrent;

namespace Intents;

/// <summary>
/// Fails fast when a named circuit is open after consecutive failures.
/// </summary>
public sealed class CircuitBreakerPolicy : IntentPolicy
{
    private static readonly ConcurrentDictionary<string, CircuitState> Circuits = new();

    public CircuitBreakerPolicy(string name, int failureThreshold, TimeSpan breakDuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (failureThreshold < 1)
            throw new ArgumentOutOfRangeException(nameof(failureThreshold));
        if (breakDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(breakDuration));

        Name = name;
        FailureThreshold = failureThreshold;
        BreakDuration = breakDuration;
    }

    public string Name { get; }
    public int FailureThreshold { get; }
    public TimeSpan BreakDuration { get; }
    public int Order => -2;

    public Func<CancellationToken, Task> Wrap(Func<CancellationToken, Task> next)
    {
        var name = Name;
        var threshold = FailureThreshold;
        var breakDuration = BreakDuration;
        var state = Circuits.GetOrAdd(name, _ => new CircuitState());

        return async ct =>
        {
            state.ThrowIfOpen(breakDuration, name);

            try
            {
                await next(ct).ConfigureAwait(false);
                state.OnSuccess();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                state.OnFailure(threshold);
                throw;
            }
        };
    }

    public static void Reset(string name)
    {
        if (Circuits.TryGetValue(name, out var state))
            state.Reset();
    }

    public static void ResetAll() => Circuits.Clear();

    private sealed class CircuitState
    {
        private int _failures;
        private long _openedAtTicks; // 0 = closed
        private int _halfOpenProbeInFlight;

        public void ThrowIfOpen(TimeSpan breakDuration, string name)
        {
            var openedAt = Interlocked.Read(ref _openedAtTicks);
            if (openedAt == 0)
                return;

            var openedAtTime = new DateTime(openedAt, DateTimeKind.Utc);
            if (DateTime.UtcNow - openedAtTime < breakDuration)
                throw new IntentCircuitOpenException(name, breakDuration - (DateTime.UtcNow - openedAtTime));

            // Half-open: allow a single probe
            if (Interlocked.CompareExchange(ref _halfOpenProbeInFlight, 1, 0) != 0)
                throw new IntentCircuitOpenException(name, TimeSpan.Zero);
        }

        public void OnSuccess()
        {
            Interlocked.Exchange(ref _failures, 0);
            Interlocked.Exchange(ref _openedAtTicks, 0);
            Interlocked.Exchange(ref _halfOpenProbeInFlight, 0);
        }

        public void OnFailure(int threshold)
        {
            Interlocked.Exchange(ref _halfOpenProbeInFlight, 0);
            var failures = Interlocked.Increment(ref _failures);
            if (failures >= threshold)
                Interlocked.Exchange(ref _openedAtTicks, DateTime.UtcNow.Ticks);
        }

        public void Reset()
        {
            Interlocked.Exchange(ref _failures, 0);
            Interlocked.Exchange(ref _openedAtTicks, 0);
            Interlocked.Exchange(ref _halfOpenProbeInFlight, 0);
        }
    }
}

public sealed class IntentCircuitOpenException : InvalidOperationException
{
    public IntentCircuitOpenException(string circuitName, TimeSpan retryAfter)
        : base(retryAfter <= TimeSpan.Zero
            ? $"Circuit '{circuitName}' is open (half-open probe already in flight)."
            : $"Circuit '{circuitName}' is open. Retry after ~{retryAfter}.")
    {
        CircuitName = circuitName;
        RetryAfter = retryAfter;
    }

    public string CircuitName { get; }
    public TimeSpan RetryAfter { get; }
}
