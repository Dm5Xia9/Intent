namespace Intents;

/// <summary>
/// Links an external <see cref="CancellationToken"/> into the execution pipeline.
/// </summary>
public sealed class CancelPolicy : IntentPolicy
{
    public CancelPolicy(CancellationToken token) => Token = token;

    public CancellationToken Token { get; }
    public int Order => -20;

    public Func<CancellationToken, Task> Wrap(Func<CancellationToken, Task> next)
    {
        var token = Token;
        return async ct =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, token);
            await next(linked.Token).ConfigureAwait(false);
        };
    }
}
