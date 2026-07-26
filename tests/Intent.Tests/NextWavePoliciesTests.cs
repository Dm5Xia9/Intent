using System.Diagnostics;
using System.Threading.Channels;

namespace Intents.Tests;

[Collection("Intent")]
public class NextWavePoliciesTests
{
    public NextWavePoliciesTests()
    {
        IntentCacheStore.Clear();
        IntentDiagnostics.Reset();
    }

    [Fact]
    public async Task WhenAll_runs_concurrently()
    {
        var started = 0;
        var max = 0;

        Intent Make() => Intent.From(async () =>
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
            Intent.From(() => log.Add(1)),
            Intent.From(() => log.Add(2)),
            Intent.From(() => log.Add(3)));
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

        Intent.From(() => throw new InvalidOperationException("bg"))
            .Background();

        await faulted.Task.WaitAsync(2.Seconds());
    }

    [Fact]
    public async Task Into_writes_result_to_channel()
    {
        var channel = Channel.CreateUnbounded<int>();

        Intent.From(() => 42).Into(channel);

        Assert.Equal(42, await channel.Reader.ReadAsync().AsTask().WaitAsync(2.Seconds()));
    }

    [Fact]
    public async Task Into_writer_overload_and_fault_reports_trace()
    {
        var channel = Channel.CreateUnbounded<int>();
        var faulted = new TaskCompletionSource();
        IntentDiagnostics.Traced += e =>
        {
            if (e.Phase == IntentTracePhase.Faulted)
                faulted.TrySetResult();
        };

        Intent.From((Func<int>)(() => throw new InvalidOperationException("into")))
            .Into(channel.Writer);

        await faulted.Task.WaitAsync(2.Seconds());
        Assert.False(channel.Reader.TryRead(out _));
    }

    [Fact]
    public async Task FromEach_runs_body_per_item()
    {
        var channel = Channel.CreateUnbounded<int>();
        var seen = new List<int>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        channel.FromEach(
            n => Intent.From(() => { lock (seen) seen.Add(n); }),
            CancellationToken.None,
            onCompleted: () => done.TrySetResult());

        await channel.Writer.WriteAsync(1);
        await channel.Writer.WriteAsync(2);
        await channel.Writer.WriteAsync(3);
        channel.Writer.TryComplete();

        await done.Task.WaitAsync(2.Seconds());
        Assert.Equal(new[] { 1, 2, 3 }, seen);
    }

    [Fact]
    public async Task FromEach_into_forwards_results_and_completes_writer()
    {
        var input = Channel.CreateUnbounded<int>();
        var output = Channel.CreateUnbounded<int>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        input.FromEach(
            n => Intent.From(() => n * 10),
            CancellationToken.None,
            into: output.Writer,
            onCompleted: () => done.TrySetResult());

        await input.Writer.WriteAsync(2);
        await input.Writer.WriteAsync(3);
        input.Writer.TryComplete();

        await done.Task.WaitAsync(2.Seconds());

        Assert.Equal(20, await output.Reader.ReadAsync().AsTask().WaitAsync(2.Seconds()));
        Assert.Equal(30, await output.Reader.ReadAsync().AsTask().WaitAsync(2.Seconds()));
        await output.Reader.Completion.WaitAsync(2.Seconds());
    }

    [Fact]
    public async Task FromEach_item_fault_reports_trace_and_continues()
    {
        var channel = Channel.CreateUnbounded<int>();
        var seen = new List<int>();
        var faults = 0;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        IntentDiagnostics.Traced += e =>
        {
            if (e.Phase == IntentTracePhase.Faulted)
                Interlocked.Increment(ref faults);
        };

        channel.FromEach(
            n => Intent.From(() =>
            {
                if (n == 2)
                    throw new InvalidOperationException("boom");
                lock (seen) seen.Add(n);
            }),
            CancellationToken.None,
            onCompleted: () => done.TrySetResult());

        await channel.Writer.WriteAsync(1);
        await channel.Writer.WriteAsync(2);
        await channel.Writer.WriteAsync(3);
        channel.Writer.TryComplete();

        await done.Task.WaitAsync(2.Seconds());
        Assert.Equal(new[] { 1, 3 }, seen);
        Assert.True(faults >= 1);
    }

    [Fact]
    public async Task Activity_creates_diagnostic_activity()
    {
        Activity? seen = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == IntentInstrumentation.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = a => seen = a
        };
        ActivitySource.AddActivityListener(listener);

        await Intent.From(() => { })
            .Configure(IntentPolicies.Named("ActDemo"), IntentPolicies.Activity);

        Assert.NotNull(seen);
        Assert.Equal("ActDemo", seen!.DisplayName);
    }
}
