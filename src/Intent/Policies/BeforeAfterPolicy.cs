namespace Intents;

/// <summary>
/// Runs a hook immediately before the intent body (inside Retry / Bulkhead / Atomic).
/// Re-executes on every Retry attempt.
/// </summary>
public sealed class BeforePolicy : IntentPolicy
{
    private readonly Func<CancellationToken, Task> _hook;

    public BeforePolicy(Func<CancellationToken, Task> hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        _hook = hook;
    }

    public int Order => 4;

    public Func<CancellationToken, Task> Wrap(Func<CancellationToken, Task> next)
    {
        var hook = _hook;
        return async ct =>
        {
            await hook(ct).ConfigureAwait(false);
            await next(ct).ConfigureAwait(false);
        };
    }
}

/// <summary>
/// Runs a hook immediately after each attempt of the intent body (success or fault),
/// inside Retry / Bulkhead / Atomic. Re-executes on every Retry attempt.
/// </summary>
public sealed class AfterPolicy : IntentPolicy
{
    private readonly Func<CancellationToken, Task> _hook;

    public AfterPolicy(Func<CancellationToken, Task> hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        _hook = hook;
    }

    public int Order => 5;

    public Func<CancellationToken, Task> Wrap(Func<CancellationToken, Task> next)
    {
        var hook = _hook;
        return async ct =>
        {
            try
            {
                await next(ct).ConfigureAwait(false);
            }
            finally
            {
                await hook(ct).ConfigureAwait(false);
            }
        };
    }
}
