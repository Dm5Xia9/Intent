using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Intents;

/// <summary>
/// Records execution count and duration via <see cref="System.Diagnostics.Metrics"/>
/// (<see cref="IntentInstrumentation"/>).
/// </summary>
public sealed class MetricsPolicy : IntentPolicy
{
    public static MetricsPolicy Instance { get; } = new();

    private MetricsPolicy() { }

    public int Order => IntentPipelineOrder.Metrics;

    public Func<CancellationToken, Task> Wrap(Func<CancellationToken, Task> next)
    {
        return async ct =>
        {
            var name = IntentAmbient.Name;
            var sw = Stopwatch.StartNew();
            var success = false;
            try
            {
                await next(ct).ConfigureAwait(false);
                success = true;
            }
            finally
            {
                sw.Stop();
                var outcome = success ? "success" : "failure";
                var tagList = BuildTags(name, outcome);
                IntentInstrumentation.ExecutionCount.Add(1, tagList);
                IntentInstrumentation.ExecutionDuration.Record(sw.Elapsed.TotalMilliseconds, tagList);
            }
        };
    }

    private static TagList BuildTags(string intentName, string outcome)
    {
        var tags = new TagList
        {
            { "intent.name", intentName },
            { "intent.outcome", outcome }
        };

        foreach (var (key, value) in IntentAmbient.Tags)
        {
            if (value is null)
                continue;
            // Skip reserved keys already set
            if (key is "intent.name" or "intent.outcome")
                continue;
            tags.Add(key, value);
        }

        return tags;
    }
}
