using System.Diagnostics;

namespace Intents.Tests;

[Collection("Intent")]
public class DxWaveTests
{
    public DxWaveTests()
    {
        IntentMetrics.Reset();
        IntentCacheStore.Clear();
        IntentIdempotencyStore.Clear();
        IntentDiagnostics.Reset();
        CircuitBreakerPolicy.ResetAll();
    }

    [Fact]
    public async Task Idempotent_void_runs_body_once()
    {
        var calls = 0;
        var policy = IntentPolicies.Idempotent("void-once", 5.Seconds());

        await Intent.From(() => Interlocked.Increment(ref calls)).Configure(policy);
        await Intent.From(() => Interlocked.Increment(ref calls)).Configure(policy);

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Idempotent_result_returns_cached_value()
    {
        var calls = 0;
        var policy = IntentPolicies.Idempotent("result-once", 5.Seconds());

        var a = await Intent.From(() =>
        {
            Interlocked.Increment(ref calls);
            return 7;
        }).Configure(policy);

        var b = await Intent.From(() =>
        {
            Interlocked.Increment(ref calls);
            return 99;
        }).Configure(policy);

        Assert.Equal(7, a);
        Assert.Equal(7, b);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Idempotent_shares_in_flight_execution()
    {
        var calls = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var policy = IntentPolicies.Idempotent("inflight", 5.Seconds());

        async Task<int> Start() =>
            await Intent.From(async () =>
            {
                Interlocked.Increment(ref calls);
                await gate.Task;
                return 42;
            }).Configure(policy);

        var t1 = Start();
        var t2 = Start();
        await Task.Delay(30);
        Assert.Equal(1, calls);
        gate.SetResult();

        Assert.Equal(42, await t1);
        Assert.Equal(42, await t2);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Idempotent_failure_does_not_stick()
    {
        var calls = 0;
        var policy = IntentPolicies.Idempotent("fail-retry", 5.Seconds());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Intent.From(() =>
            {
                Interlocked.Increment(ref calls);
                throw new InvalidOperationException("boom");
            }).Configure(policy));

        await Intent.From(() => Interlocked.Increment(ref calls)).Configure(policy);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Tag_appears_on_trace_and_activity()
    {
        IReadOnlyDictionary<string, object?>? seen = null;
        IntentDiagnostics.Traced += e =>
        {
            if (e.Phase == IntentTracePhase.Started)
                seen = e.Tags;
        };

        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "Intents.Intent",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = a =>
            {
                Assert.Equal("u1", a.GetTagItem("userId"));
            }
        };
        ActivitySource.AddActivityListener(listener);

        await Intent.From(() => { })
            .Configure(
                IntentPolicies.Named("Tagged"),
                IntentPolicies.Tag("userId", "u1"),
                IntentPolicies.Trace,
                IntentPolicies.Activity);

        Assert.NotNull(seen);
        Assert.Equal("u1", seen!["userId"]);
    }

    [Fact]
    public async Task Profile_Http_applies_named_metrics()
    {
        await Intent.From(() => { }).Configure(IntentProfile.Http("demo-http"));
        Assert.Equal(1, IntentMetrics.GetCount("demo-http"));
    }

    [Fact]
    public async Task Profile_DbWrite_serializes_on_atomic_key()
    {
        var depth = 0;
        var max = 0;

        Intent Make() => Intent.From(async () =>
        {
            var n = Interlocked.Increment(ref depth);
            max = Math.Max(max, n);
            await Task.Delay(40);
            Interlocked.Decrement(ref depth);
        }).Configure(IntentProfile.DbWrite("dbw", atomicKey: "dbw"));

        await Intent.WhenAll(Make(), Make(), Make());
        Assert.Equal(1, max);
    }

    [Fact]
    public async Task Then_and_Select_chain_results()
    {
        var result = await Intent.From(() => 2)
            .Then(x => Intent.From(() => x * 3))
            .Select(x => x + 1);

        Assert.Equal(7, result);
    }

    [Fact]
    public async Task Then_with_task_delegate()
    {
        var result = await Intent.From(() => 5)
            .Then(async (x, _) =>
            {
                await Task.Yield();
                return x * 2;
            });

        Assert.Equal(10, result);
    }
}
