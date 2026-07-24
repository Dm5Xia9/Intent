namespace Intents;

public sealed class RetryPolicy : IntentPolicy
{
    public RetryPolicy(
        int attempts,
        Func<int, TimeSpan>? backoff = null,
        Func<Exception, bool>? shouldRetry = null,
        TimeSpan? attemptTimeout = null)
    {
        if (attempts < 1)
            throw new ArgumentOutOfRangeException(nameof(attempts), "Attempts must be at least 1.");
        if (attemptTimeout is { } t && t <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(attemptTimeout), "Attempt timeout must be positive.");

        Attempts = attempts;
        Backoff = backoff;
        ShouldRetry = shouldRetry ?? (static ex => ex is not OperationCanceledException);
        AttemptTimeout = attemptTimeout;
    }

    public int Attempts { get; }
    public Func<int, TimeSpan>? Backoff { get; }
    public Func<Exception, bool> ShouldRetry { get; }
    public TimeSpan? AttemptTimeout { get; }
    public int Order => 1;

    public Func<CancellationToken, Task> Wrap(Func<CancellationToken, Task> next)
    {
        var attempts = Attempts;
        var backoff = Backoff;
        var shouldRetry = ShouldRetry;
        var attemptTimeout = AttemptTimeout;
        return async ct =>
        {
            Exception? last = null;
            for (var i = 0; i < attempts; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (i > 0 && backoff is not null)
                {
                    var delay = backoff(i);
                    if (delay > TimeSpan.Zero)
                        await Task.Delay(delay, ct).ConfigureAwait(false);
                }

                try
                {
                    if (attemptTimeout is { } timeout)
                    {
                        try
                        {
                            await next(ct).WaitAsync(timeout, ct).ConfigureAwait(false);
                        }
                        catch (TimeoutException)
                        {
                            throw new TimeoutException($"Attempt timed out after {timeout}.");
                        }
                    }
                    else
                    {
                        await next(ct).ConfigureAwait(false);
                    }

                    return;
                }
                catch (Exception ex) when (shouldRetry(ex))
                {
                    last = ex;
                }
            }

            throw last!;
        };
    }
}

public static class IntentBackoff
{
    /// <summary>
    /// Backoff before attempt <paramref name="attempt"/> (1 = first retry after failure).
    /// </summary>
    public static Func<int, TimeSpan> Exponential(
        TimeSpan initial,
        double factor = 2.0,
        TimeSpan? max = null)
    {
        if (initial < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(initial));
        if (factor < 1.0)
            throw new ArgumentOutOfRangeException(nameof(factor));

        var maxDelay = max ?? TimeSpan.FromMinutes(1);
        return attempt =>
        {
            var ms = initial.TotalMilliseconds * Math.Pow(factor, attempt - 1);
            var delay = TimeSpan.FromMilliseconds(ms);
            return delay > maxDelay ? maxDelay : delay;
        };
    }

    public static Func<int, TimeSpan> Constant(TimeSpan delay) => _ => delay;
}
