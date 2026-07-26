namespace Intents.Time;

/// <summary>
/// Opt-in TimeSpan helpers. Import with <c>using Intents.Time;</c> — not in the root <c>Intents</c> namespace.
/// </summary>
public static class IntentTime
{
    public static TimeSpan Seconds(this int value) => TimeSpan.FromSeconds(value);

    public static TimeSpan Milliseconds(this int value) => TimeSpan.FromMilliseconds(value);
}
