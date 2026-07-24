using System.Diagnostics;

namespace Intents;

/// <summary>
/// Starts a <see cref="Activity"/> around the operation (OpenTelemetry-compatible via ActivitySource).
/// </summary>
public sealed class ActivityPolicy : IntentPolicy
{
    public static ActivityPolicy Instance { get; } = new();

    internal static readonly ActivitySource Source = new("Intents.Intent");

    private ActivityPolicy() { }

    public int Order => -8;

    public Func<CancellationToken, Task> Wrap(Func<CancellationToken, Task> next)
    {
        return async ct =>
        {
            var name = IntentAmbient.Name;
            ActivityTagsCollection? tags = null;
            if (IntentAmbient.Tags.Count > 0)
            {
                tags = new ActivityTagsCollection();
                foreach (var (key, value) in IntentAmbient.Tags)
                    tags[key] = value;
            }

            using var activity = Source.StartActivity(
                name,
                ActivityKind.Internal,
                default(ActivityContext),
                tags);

            try
            {
                await next(ct).ConfigureAwait(false);
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity?.SetTag("exception.type", ex.GetType().FullName);
                activity?.SetTag("exception.message", ex.Message);
                throw;
            }
        };
    }
}
