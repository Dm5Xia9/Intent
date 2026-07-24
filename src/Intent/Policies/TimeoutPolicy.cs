namespace Intents;

public sealed class TimeoutPolicy : IntentPolicy
{
    public TimeoutPolicy(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");
        Timeout = timeout;
    }

    public TimeSpan Timeout { get; }
    public int Order => 0;

    public Func<CancellationToken, Task> Wrap(Func<CancellationToken, Task> next)
    {
        var timeout = Timeout;
        return async ct =>
        {
            try
            {
                await next(ct).WaitAsync(timeout, ct).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"Operation timed out after {timeout}.");
            }
        };
    }
}
