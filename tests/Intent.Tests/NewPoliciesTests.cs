namespace Intents.Tests;

[Collection("Intent")]
public class NewPoliciesTests
{
    public NewPoliciesTests()
    {
        IntentMetrics.Reset();
        IntentCacheStore.Clear();
        IntentDiagnostics.Reset();
    }

    [Fact]
    public async Task Cancel_stops_operation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<CancellationToken, Task> body = async ct =>
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Intent.Run(body).Configure(Intent.Cancel(cts.Token)));
    }

    [Fact]
    public async Task Cancel_is_observed_by_token_aware_body()
    {
        using var cts = new CancellationTokenSource();
        var entered = false;

        Func<CancellationToken, Task> body = async ct =>
        {
            entered = true;
            await Task.Delay(5.Seconds(), ct);
        };

        cts.CancelAfter(30);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Intent.Run(body).Configure(Intent.Cancel(cts.Token)));
        Assert.True(entered);
    }

    [Fact]
    public async Task AtomicOn_isolates_keys()
    {
        var a = 0;
        var b = 0;
        var maxA = 0;
        var maxB = 0;

        async Task WorkA()
        {
            Func<Task> body = async () =>
            {
                var n = Interlocked.Increment(ref a);
                maxA = Math.Max(maxA, n);
                await Task.Delay(40);
                Interlocked.Decrement(ref a);
            };
            await Intent.Run(body).Configure(Intent.AtomicOn("a"));
        }

        async Task WorkB()
        {
            Func<Task> body = async () =>
            {
                var n = Interlocked.Increment(ref b);
                maxB = Math.Max(maxB, n);
                await Task.Delay(40);
                Interlocked.Decrement(ref b);
            };
            await Intent.Run(body).Configure(Intent.AtomicOn("b"));
        }

        await Task.WhenAll(WorkA(), WorkA(), WorkB(), WorkB());
        Assert.Equal(1, maxA);
        Assert.Equal(1, maxB);
    }

    [Fact]
    public async Task Retry_with_backoff_delays_between_attempts()
    {
        var attempts = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        await Intent.Run(() =>
            {
                attempts++;
                if (attempts < 3)
                    throw new InvalidOperationException("x");
            })
            .Configure(Intent.Retry(3, IntentBackoff.Constant(30.Milliseconds())));

        sw.Stop();
        Assert.Equal(3, attempts);
        Assert.True(sw.ElapsedMilliseconds >= 50, $"elapsed={sw.ElapsedMilliseconds}");
    }

    [Fact]
    public async Task Retry_filter_skips_non_matching_exceptions()
    {
        var attempts = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Intent.Run(() =>
                {
                    attempts++;
                    throw new InvalidOperationException("nope");
                })
                .Configure(Intent.Retry(5, shouldRetry: ex => ex is TimeoutException)));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Named_Trace_Metrics_emit_events()
    {
        var traces = new List<IntentTraceEvent>();
        var metrics = new List<IntentMetricEvent>();
        IntentDiagnostics.Traced += e => traces.Add(e);
        IntentDiagnostics.Measured += e => metrics.Add(e);

        await Intent.Run(() => { })
            .Configure(Intent.Named("Checkout"), Intent.Trace, Intent.Metrics);

        Assert.Contains(traces, t => t.Name == "Checkout" && t.Phase == IntentTracePhase.Started);
        Assert.Contains(traces, t => t.Name == "Checkout" && t.Phase == IntentTracePhase.Succeeded);
        Assert.Contains(metrics, m => m.Name == "Checkout" && m.Success);
        Assert.Equal(1, IntentMetrics.GetCount("Checkout"));
        Assert.Equal(1, IntentMetrics.GetSuccesses("Checkout"));
    }

    [Fact]
    public async Task Cache_returns_cached_value_without_rerunning_body()
    {
        var calls = 0;
        Func<int> body = () =>
        {
            calls++;
            return 7;
        };

        Assert.Equal(7, await Intent.Run(body).Configure(Intent.Cache("k1", 1.Seconds())));
        Assert.Equal(7, await Intent.Run(body).Configure(Intent.Cache("k1", 1.Seconds())));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Cache_expires()
    {
        var calls = 0;
        Func<int> body = () =>
        {
            calls++;
            return calls;
        };

        Assert.Equal(1, await Intent.Run(body).Configure(Intent.Cache("exp", 40.Milliseconds())));
        await Task.Delay(60);
        Assert.Equal(2, await Intent.Run(body).Configure(Intent.Cache("exp", 40.Milliseconds())));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Atomically_with_key_runs_under_keyed_lock()
    {
        var ran = false;
        await Intent.Atomically("order:1", () => ran = true);
        Assert.True(ran);
    }
}
