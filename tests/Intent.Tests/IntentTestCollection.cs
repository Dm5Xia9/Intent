namespace Intents.Tests;

/// <summary>
/// Intent uses process-wide static stores (cache, idempotency, diagnostics).
/// Run tests in this collection sequentially.
/// </summary>
[CollectionDefinition("Intent")]
public sealed class IntentTestCollection;
