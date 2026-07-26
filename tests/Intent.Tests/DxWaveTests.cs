using System.Diagnostics;

namespace Intents.Tests;

[Collection("Intent")]
public class DxWaveTests
{
    public DxWaveTests()
    {
        IntentCacheStore.Clear();
        IntentIdempotencyStore.Clear();
        IntentDiagnostics.Reset();
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
    public async Task Tag_appears_on_activity_tags_and_baggage()
    {
        IReadOnlyDictionary<string, object?>? seen = null;
        IntentDiagnostics.Traced += e =>
        {
            if (e.Phase == IntentTracePhase.Started)
                seen = e.Tags;
        };

        Activity? activity = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == IntentInstrumentation.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = a => activity = a
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
        Assert.NotNull(activity);
        Assert.Equal("u1", activity!.GetTagItem("userId"));
        Assert.Equal("u1", activity.GetBaggageItem("userId"));
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
