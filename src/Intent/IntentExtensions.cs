namespace Intents;

public static class IntentExtensions
{
    /// <summary>
    /// Schedules the intent without awaiting. Faults go to <see cref="IntentDiagnostics"/>.
    /// </summary>
    public static void Background(this Intent intent) => Intent.Background(intent);
}
