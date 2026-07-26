using System.Diagnostics;
using System.Globalization;

namespace Intents;

/// <summary>
/// Starts a <see cref="Activity"/> around the operation (OpenTelemetry-compatible via
/// <see cref="IntentInstrumentation.ActivitySource"/>). Ambient <see cref="IntentAmbient.Tags"/>
/// become span tags and baggage for correlation.
/// </summary>
public sealed class ActivityPolicy : IntentPolicy
{
    public static ActivityPolicy Instance { get; } = new();

    private ActivityPolicy() { }

    public int Order => IntentPipelineOrder.Activity;

    public Func<CancellationToken, Task> Wrap(Func<CancellationToken, Task> next)
    {
        return async ct =>
        {
            var name = IntentAmbient.Name;

            // Inherit Activity.Current so Aspire / Jaeger parent-child links work.
            using var activity = IntentInstrumentation.ActivitySource.StartActivity(
                name,
                ActivityKind.Internal);

            if (activity is not null)
            {
                foreach (var (key, value) in IntentAmbient.Tags)
                {
                    activity.SetTag(key, value);
                    if (value is not null)
                    {
                        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
                        if (!string.IsNullOrEmpty(text))
                            activity.SetBaggage(key, text);
                    }
                }
            }

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
