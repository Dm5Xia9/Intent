using System.Diagnostics;

namespace Intents.Tests;

[Collection("Intent")]
public class NextWavePoliciesTests
{
    public NextWavePoliciesTests()
    {
        IntentMetrics.Reset();
        IntentCacheStore.Clear();
        IntentDiagnostics.Reset();
        CircuitBreakerPolicy.ResetAll();
    }

    [Fact]
    public async Task Retry_attemptTimeout_fails_slow_attempt_and_retries()
    {
        var attempts = 0;
        Func<Task> body = async () =>
        {
            attempts++;
            if (attempts < 2)
                await Task.Delay(200);
        };

        await Intent.Run(body)
            .Useful(Intent.Retry(3, attemptTimeout: 40.Milliseconds()));

        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task CircuitBreaker_opens_after_threshold()
    {
        Func<Task> boom = () => throw new InvalidOperationException("x");

        for (var i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await Intent.Run(boom).Useful(Intent.CircuitBreaker("cb1", failureThreshold: 3, breakDuration: 5.Seconds())));
        }

        await Assert.ThrowsAsync<IntentCircuitOpenException>(async () =>
            await Intent.Run(boom).Useful(Intent.CircuitBreaker("cb1", failureThreshold: 3, breakDuration: 5.Seconds())));
    }

    [Fact]
    public async Task CircuitBreaker_half_open_probe_can_close()
    {
        Func<Task> boom = () => throw new InvalidOperationException("x");
        var policy = Intent.CircuitBreaker("cb2", failureThreshold: 2, breakDuration: 30.Milliseconds());

        for (var i = 0; i < 2; i++)
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await Intent.Run(boom).Useful(policy));

        await Assert.ThrowsAsync<IntentCircuitOpenException>(async () =>
            await Intent.Run(boom).Useful(policy));

        await Task.Delay(40);

        await Intent.Run(() => { }).Useful(policy);
        await Intent.Run(() => { }).Useful(policy);
    }

    [Fact]
    public async Task WhenAll_runs_concurrently()
    {
        var started = 0;
        var max = 0;

        Intent Make() => Intent.Run(async () =>
        {
            var n = Interlocked.Increment(ref started);
            max = Math.Max(max, n);
            await Task.Delay(40);
            Interlocked.Decrement(ref started);
        });

        await Intent.WhenAll(Make(), Make(), Make());
        Assert.True(max >= 2, $"max={max}");
    }

    [Fact]
    public async Task Sequence_runs_in_order()
    {
        var log = new List<int>();
        await Intent.Sequence(
            Intent.Run(() => log.Add(1)),
            Intent.Run(() => log.Add(2)),
            Intent.Run(() => log.Add(3)));
        Assert.Equal(new[] { 1, 2, 3 }, log);
    }

    [Fact]
    public async Task Background_runs_without_awaiter_and_reports_fault()
    {
        var faulted = new TaskCompletionSource();
        IntentDiagnostics.Traced += e =>
        {
            if (e.Phase == IntentTracePhase.Faulted)
                faulted.TrySetResult();
        };

        Intent.Run(() => throw new InvalidOperationException("bg"))
            .Background();

        await faulted.Task.WaitAsync(2.Seconds());
    }

    [Fact]
    public async Task Activity_creates_diagnostic_activity()
    {
        Activity? seen = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "Intents.Intent",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = a => seen = a
        };
        ActivitySource.AddActivityListener(listener);

        await Intent.Run(() => { })
            .Useful(Intent.Named("ActDemo"), Intent.Activity);

        Assert.NotNull(seen);
        Assert.Equal("ActDemo", seen!.DisplayName);
    }

    [Fact]
    public async Task Bulkhead_limits_parallelism()
    {
        var inFlight = 0;
        var max = 0;

        async Task Work()
        {
            Func<Task> body = async () =>
            {
                var n = Interlocked.Increment(ref inFlight);
                max = Math.Max(max, n);
                await Task.Delay(40);
                Interlocked.Decrement(ref inFlight);
            };
            await Intent.Run(body).Useful(Intent.Bulkhead("bh", maxParallelism: 2));
        }

        await Task.WhenAll(Work(), Work(), Work(), Work());
        Assert.Equal(2, max);
    }
}
