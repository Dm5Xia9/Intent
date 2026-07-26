using Polly;

namespace Intents.Polly;

/// <summary>
/// Wraps a Polly <see cref="ResiliencePipeline"/> as an <see cref="IntentPolicy"/>.
/// </summary>
public sealed class PollyPolicy : IntentPolicy
{
    private readonly ResiliencePipeline _pipeline;

    public PollyPolicy(ResiliencePipeline pipeline, int order = 0)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        _pipeline = pipeline;
        Order = order;
    }

    public int Order { get; }

    public Func<CancellationToken, Task> Wrap(Func<CancellationToken, Task> next)
    {
        var pipeline = _pipeline;
        return async ct =>
        {
            await pipeline.ExecuteAsync(
                static async (state, token) => await state(token).ConfigureAwait(false),
                next,
                ct).ConfigureAwait(false);
        };
    }
}
