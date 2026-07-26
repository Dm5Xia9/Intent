using System.Diagnostics.Metrics;

namespace Intents.Tests;

[Collection("Intent")]
public class NewPoliciesTests
{
    public NewPoliciesTests()
    {
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
            await Intent.From(body).WithCancel(cts.Token));
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
            await Intent.From(body).WithCancel(cts.Token));
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
            await Intent.From(body).WithAtomicOn("a");
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
            await Intent.From(body).WithAtomicOn("b");
        }

        await Task.WhenAll(WorkA(), WorkA(), WorkB(), WorkB());
        Assert.Equal(1, maxA);
        Assert.Equal(1, maxB);
    }

    [Fact]
    public async Task Named_Trace_emit_events()
    {
        var traces = new List<IntentTraceEvent>();
        IntentDiagnostics.Traced += e => traces.Add(e);

        await Intent.From(() => { })
            .Configure(IntentPolicies.Named("Checkout"), IntentPolicies.Trace);

        Assert.Contains(traces, t => t.Name == "Checkout" && t.Phase == IntentTracePhase.Started);
        Assert.Contains(traces, t => t.Name == "Checkout" && t.Phase == IntentTracePhase.Succeeded);
    }

    [Fact]
    public async Task Named_Metrics_emits_system_diagnostics_instruments()
    {
        long count = 0;
        double? duration = null;
        string? outcome = null;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == IntentInstrumentation.Name)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((inst, measurement, tags, _) =>
        {
            if (inst.Name == IntentInstrumentation.ExecutionCountInstrument)
            {
                count += measurement;
                foreach (var tag in tags)
                {
                    if (tag.Key == "intent.outcome")
                        outcome = tag.Value?.ToString();
                }
            }
        });
        listener.SetMeasurementEventCallback<double>((inst, measurement, tags, _) =>
        {
            if (inst.Name == IntentInstrumentation.ExecutionDurationInstrument)
                duration = measurement;
        });
        listener.Start();

        await Intent.From(() => { })
            .Configure(IntentPolicies.Named("Checkout"), IntentPolicies.Metrics);

        Assert.Equal(1, count);
        Assert.Equal("success", outcome);
        Assert.NotNull(duration);
        Assert.True(duration >= 0);
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

        Assert.Equal(7, await Intent.From(body).WithCache("k1", 1.Seconds()));
        Assert.Equal(7, await Intent.From(body).WithCache("k1", 1.Seconds()));
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

        Assert.Equal(1, await Intent.From(body).WithCache("exp", 40.Milliseconds()));
        await Task.Delay(60);
        Assert.Equal(2, await Intent.From(body).WithCache("exp", 40.Milliseconds()));
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
