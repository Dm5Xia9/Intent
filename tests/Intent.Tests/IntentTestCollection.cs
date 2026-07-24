namespace Intents.Tests;

/// <summary>
/// Intent uses process-wide static stores (metrics, cache, circuit, diagnostics).
/// Run tests in this collection sequentially.
/// </summary>
[CollectionDefinition("Intent")]
public sealed class IntentTestCollection;
