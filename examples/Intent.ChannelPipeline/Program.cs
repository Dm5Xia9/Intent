using System.Threading.Channels;
using Intents;
using Intents.ChannelPipeline;
using Intents.Polly;
using Intents.Time;

/*
 * Channel pipeline with Into (produce) + FromEach (consume):
 *
 *   catalog ──┐
 *   pricing ──┼─ Into(fragments) ─► join ─► enriched ─FromEach(Score)─► ranked ─FromEach─► print
 *   reviews ──┘
 */

Console.WriteLine("=== Intent channel pipeline ===\n");

IntentDiagnostics.Traced += e =>
{
    if (e.Phase == IntentTracePhase.Faulted)
        Console.WriteLine($"  ! fault [{e.Name}]: {e.Exception?.Message}");
};

var productIds = new[] { "sku-100", "sku-200", "sku-300", "sku-404" };
var expectedProducts = productIds.Count(id => !id.EndsWith("404", StringComparison.Ordinal));

var fragments = Channel.CreateUnbounded<SourceFragment>(new UnboundedChannelOptions
{
    SingleReader = true,
    SingleWriter = false
});
var enriched = Channel.CreateUnbounded<EnrichedProduct>(new UnboundedChannelOptions
{
    SingleReader = true,
    SingleWriter = true
});
var ranked = Channel.CreateUnbounded<RankedProduct>(new UnboundedChannelOptions
{
    SingleReader = true,
    SingleWriter = true
});

using var cts = new CancellationTokenSource();
var ct = cts.Token;

var joinTask = Pipeline.JoinAsync(fragments.Reader, enriched.Writer, expectedProducts, ct);

var rankDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
enriched.FromEach(
    product => Sources.Score(product).WithNamed("rank"),
    ct,
    into: ranked.Writer,
    onCompleted: () => rankDone.TrySetResult());

var rows = new List<RankedProduct>();
var printDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
ranked.FromEach(
    row => Collect(row),
    ct,
    onCompleted: () =>
    {
        Pipeline.PrintTable(rows);
        printDone.TrySetResult();
    });

Console.WriteLine("Launching background sources via .Into(fragments)...\n");

foreach (var id in productIds)
{
    Sources.FetchCatalog(id)
        .WithNamed($"catalog:{id}")
        .WithRetry(2, IntentBackoff.Constant(30.Milliseconds()))
        .WithTimeout(2.Seconds())
        .Into(fragments);

    Sources.FetchPricing(id)
        .WithNamed($"pricing:{id}")
        .WithRetry(2, IntentBackoff.Constant(30.Milliseconds()))
        .WithTimeout(2.Seconds())
        .Into(fragments);

    Sources.FetchReviews(id)
        .WithNamed($"reviews:{id}")
        .WithRetry(2, IntentBackoff.Constant(30.Milliseconds()))
        .WithTimeout(2.Seconds())
        .Into(fragments);
}

await Task.WhenAll(joinTask, rankDone.Task, printDone.Task);
await Task.Delay(400);
fragments.Writer.TryComplete();

Console.WriteLine("\nDone.");

async Intent Collect(RankedProduct row)
{
    rows.Add(row);
    await Task.Yield();
}
