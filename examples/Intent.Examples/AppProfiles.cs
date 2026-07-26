using Intents.Polly;
using Intents.Time;

namespace Intents.Examples;

/// <summary>
/// Example custom profile: a reusable IntentPolicy[] pack (same idea as IntentPolly.Http).
/// </summary>
static class AppProfiles
{
    /// <summary>
    /// Named worker with console log, short retry, and overall timeout.
    /// </summary>
    public static IntentPolicy[] Worker(
        string name = "worker",
        int retryAttempts = 2,
        TimeSpan? timeout = null) =>
    [
        IntentPolicies.Named(name),
        new ConsoleLogPolicy(),
        IntentPolly.Retry(retryAttempts, IntentBackoff.Constant(15.Milliseconds())),
        IntentPolly.Timeout(timeout ?? 5.Seconds()),
    ];
}
