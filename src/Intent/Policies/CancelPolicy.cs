namespace Intents;

/// <summary>
/// Links an external <see cref="CancellationToken"/> into the execution pipeline and
/// <see cref="IntentAmbient.Token"/> so token-aware bodies and <c>async Intent</c> can observe it.
/// </summary>
public sealed class CancelPolicy : IntentPolicy
{
    public CancelPolicy(CancellationToken token) => Token = token;

    public CancellationToken Token { get; }
    public int Order => IntentPipelineOrder.Cancel;

    public Func<CancellationToken, Task> Wrap(Func<CancellationToken, Task> next)
    {
        var token = Token;
        return async ct =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, token);
            var previous = IntentAmbient.PushToken(linked.Token);
            try
            {
                await next(linked.Token).ConfigureAwait(false);
            }
            finally
            {
                IntentAmbient.RestoreToken(previous);
            }
        };
    }
}
