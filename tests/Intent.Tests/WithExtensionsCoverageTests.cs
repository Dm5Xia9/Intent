namespace Intents.Tests;

[Collection("Intent")]
public class WithExtensionsCoverageTests
{
    [Fact]
    public async Task With_star_chain_on_Intent_void()
    {
        var log = new List<string>();
        using var cts = new CancellationTokenSource();

        await Intent.From(() => log.Add("body"))
            .WithPolicies(IntentPolicies.Named("void-chain"))
            .WithCancel(cts.Token)
            .WithNamed("void-chain")
            .WithTag("k", "v")
            .WithTags(("a", 1), ("b", 2))
            .WithTrace()
            .WithActivity()
            .WithMetrics()
            .WithIdempotent("with-void-idem", 1.Seconds())
            .WithCircuitBreaker("with-void-cb")
            .WithTimeout(5.Seconds())
            .WithRetry(2)
            .WithBulkhead("with-void-bh", 4)
            .WithAtomicOn("with-void-atomic")
            .WithBefore(() => log.Add("before-sync"))
            .WithBefore(async () => { log.Add("before-task"); await Task.Yield(); })
            .WithBefore(async _ => { log.Add("before-ct"); await Task.Yield(); })
            .WithAfter(() => log.Add("after-sync"))
            .WithAfter(async () => { log.Add("after-task"); await Task.Yield(); })
            .WithAfter(async _ => { log.Add("after-ct"); await Task.Yield(); });

        Assert.Contains("body", log);
        Assert.Contains("before-sync", log);
        Assert.Contains("before-task", log);
        Assert.Contains("before-ct", log);
        Assert.Contains("after-sync", log);
        Assert.Contains("after-task", log);
        Assert.Contains("after-ct", log);
    }

    [Fact]
    public async Task With_star_chain_on_Intent_of_T()
    {
        using var cts = new CancellationTokenSource();
        var before = 0;
        var after = 0;

        var value = await Intent.From(() => 7)
            .WithPolicies(IntentPolicies.Named("t-chain"))
            .WithCancel(cts.Token)
            .WithNamed("t-chain")
            .WithTag("k", "v")
            .WithTags(("a", 1))
            .WithTrace()
            .WithActivity()
            .WithMetrics()
            .WithIdempotent("with-t-idem", 1.Seconds())
            .WithCache("with-t-cache", 1.Seconds())
            .WithCircuitBreaker("with-t-cb")
            .WithTimeout(5.Seconds())
            .WithRetry(2)
            .WithBulkhead("with-t-bh", 4)
            .WithAtomic()
            .WithAtomicOn("with-t-atomic")
            .WithBefore(() => before++)
            .WithBefore(async () => { before++; await Task.Yield(); })
            .WithBefore(async _ => { before++; await Task.Yield(); })
            .WithAfter(() => after++)
            .WithAfter(async () => { after++; await Task.Yield(); })
            .WithAfter(async _ => { after++; await Task.Yield(); });

        Assert.Equal(7, value);
        Assert.Equal(3, before);
        Assert.Equal(3, after);

        // cache hit path via WithCache on Intent<T>
        Assert.Equal(7, await Intent.From(() => 99).WithCache("with-t-cache", 1.Seconds()));
    }

    [Fact]
    public async Task IntentPolicies_Tags_and_async_Before_After_factories()
    {
        var log = new List<string>();

        await Intent.From(() => log.Add("body"))
            .Configure(
                IntentPolicies.Tags(("x", 1), ("y", 2)),
                IntentPolicies.Before(async () => { log.Add("before"); await Task.Yield(); }),
                IntentPolicies.After(async () => { log.Add("after"); await Task.Yield(); }),
                IntentPolicies.After(async _ => { log.Add("after-ct"); await Task.Yield(); }));

        Assert.Equal(["before", "body", "after-ct", "after"], log);
    }

    [Fact]
    public async Task WithCache_on_Intent_void_compiles_and_runs()
    {
        await Intent.From(() => { }).WithCache("void-cache-key", 1.Seconds());
    }

    [Fact]
    public async Task WithAtomic_on_Intent_void()
    {
        var ran = false;
        await Intent.From(() => ran = true).WithAtomic();
        Assert.True(ran);
    }
}
