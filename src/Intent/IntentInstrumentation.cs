using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Intents;

/// <summary>
/// Stable OpenTelemetry / <see cref="ActivitySource"/> / <see cref="Meter"/> names for Intent.
/// Register these in your OTel SDK (Aspire, collector, Jaeger).
/// </summary>
public static class IntentInstrumentation
{
    /// <summary>ActivitySource and Meter name: <c>Intents.Intent</c>.</summary>
    public const string Name = "Intents.Intent";

    /// <summary>Counter: <c>intents.execution.count</c> (tags: intent.name, intent.outcome).</summary>
    public const string ExecutionCountInstrument = "intents.execution.count";

    /// <summary>Histogram: <c>intents.execution.duration</c> in milliseconds (tags: intent.name, intent.outcome).</summary>
    public const string ExecutionDurationInstrument = "intents.execution.duration";

    public static ActivitySource ActivitySource { get; } = new(Name);

    public static Meter Meter { get; } = new(Name);

    internal static Counter<long> ExecutionCount { get; } =
        Meter.CreateCounter<long>(
            ExecutionCountInstrument,
            unit: "{execution}",
            description: "Number of Intent pipeline executions.");

    internal static Histogram<double> ExecutionDuration { get; } =
        Meter.CreateHistogram<double>(
            ExecutionDurationInstrument,
            unit: "ms",
            description: "Intent pipeline execution duration in milliseconds.");
}
