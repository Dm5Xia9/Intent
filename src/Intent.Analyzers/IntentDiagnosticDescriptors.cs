using Microsoft.CodeAnalysis;

namespace Intents.Analyzers;

internal static class IntentDiagnostics
{
    public const string Category = "Intent";

    public static readonly DiagnosticDescriptor DiscardWithoutBackground = new(
        id: "INT0001",
        title: "Discarded Intent without Background()",
        messageFormat: "Intent is discarded; call '.Background()' (or await it) so faults are observed",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Assigning an Intent to discard (_) does not schedule reliably for fire-and-forget. Use Intent.Background(intent) or await.");

    public static readonly DiagnosticDescriptor ConfigureAfterAwait = new(
        id: "INT0002",
        title: "Configure Intent after it was awaited",
        messageFormat: "Cannot configure Intent '{0}' after it has been awaited/scheduled",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Configure / With* throw at runtime once the Intent is Scheduled, Running, or Completed.");

    public static readonly DiagnosticDescriptor IntentNotAwaited = new(
        id: "INT0003",
        title: "Intent result is not awaited",
        messageFormat: "Intent is created but not awaited; use 'await', '.Background()', or '.Into(channel)'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A cold Intent does nothing until awaited, passed to Background(), or posted Into a channel.");
}
