using System.Runtime.CompilerServices;

namespace Intents.Tests;

[Collection("Intent")]
public class CoverageGapTests
{
    [Fact]
    public async Task Run_async_result_returns_value()
    {
        Func<Task<int>> work = async () =>
        {
            await Task.Yield();
            return 7;
        };

        Assert.Equal(7, await Intent.Run(work));
    }

    [Fact]
    public async Task Run_sync_result_returns_value()
    {
        Assert.Equal(42, await Intent.Run(() => 42));
    }

    [Fact]
    public async Task Defer_T_recreates_async_Intent_each_attempt()
    {
        var attempts = 0;

        async Intent<int> Flaky()
        {
            attempts++;
            await Task.Yield();
            if (attempts < 3)
                throw new InvalidOperationException("fail");
            return 9;
        }

        Assert.Equal(9, await Intent.Defer(() => Flaky()).Configure(Intent.Retry(3)));
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task Defer_awaits_inner_Intent()
    {
        async Intent Inner()
        {
            await Task.Yield();
        }

        await Intent.Defer(() => Inner());
    }

    [Fact]
    public async Task Atomically_async_overload()
    {
        var ran = false;
        Func<Task> body = async () =>
        {
            await Task.Yield();
            ran = true;
        };

        await Intent.Atomically(body);
        Assert.True(ran);
    }

    [Fact]
    public async Task Retry_reexecutes_async_Intent_without_Defer()
    {
        var attempts = 0;

        async Intent Flaky()
        {
            attempts++;
            await Task.Yield();
            if (attempts < 3)
                throw new InvalidOperationException("fail");
        }

        await Flaky().Configure(Intent.Retry(3));
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task Retry_reexecutes_async_Intent_T_without_Defer()
    {
        var attempts = 0;

        async Intent<int> Flaky()
        {
            attempts++;
            await Task.Yield();
            if (attempts < 3)
                throw new InvalidOperationException("fail");
            return 42;
        }

        Assert.Equal(42, await Flaky().Configure(Intent.Retry(3)));
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task Intent_T_Configure_after_schedule_throws()
    {
        var intent = Intent.Run(() => 1);
        await intent;
        Assert.Throws<InvalidOperationException>(() => intent.Configure(Intent.Retry(1)));
    }

    [Fact]
    public async Task Intent_T_Configure_configures_before_run()
    {
        var intent = Intent.Run(() => 1).Configure(Intent.Retry(1));
        Assert.Equal(IntentLifecycle.Configured, intent.Lifecycle);
        Assert.Equal(1, await intent);
    }

    [Fact]
    public async Task Intent_T_propagates_delegate_exception()
    {
        Action boom = () => throw new InvalidOperationException("nope");
        // Force Func<int> path via explicit typed factory that throws before returning
        Func<int> work = () => throw new InvalidOperationException("nope");
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Intent.Run(work));
    }

    [Fact]
    public async Task Intent_T_body_without_result_faults()
    {
        var intent = new Intent<int>();
        intent.SetBody(_ => Task.CompletedTask);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await intent);
    }

    [Fact]
    public async Task Intent_without_body_faults_on_await()
    {
        var intent = new Intent();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await intent);
    }

    [Fact]
    public void SetResult_without_state_machine_completes_outer()
    {
        var intent = new Intent();
        intent.SetResult();
        Assert.True(intent.IsCompleted);
        Assert.Equal(IntentLifecycle.Completed, intent.Lifecycle);
    }

    [Fact]
    public void SetException_without_state_machine_faults_outer()
    {
        var intent = new Intent();
        intent.SetException(new InvalidOperationException("x"));
        Assert.True(intent.IsCompleted);
        Assert.Equal(IntentLifecycle.Completed, intent.Lifecycle);
    }

    [Fact]
    public void Intent_T_SetException_without_state_machine_faults_outer()
    {
        var intent = new Intent<int>();
        intent.SetException(new InvalidOperationException("x"));
        Assert.True(intent.IsCompleted);
        Assert.Equal(IntentLifecycle.Completed, intent.Lifecycle);
    }

    [Fact]
    public async Task Schedule_second_call_is_noop()
    {
        var intent = Intent.Run(() => { });
        intent.Schedule();
        intent.Schedule();
        await intent;
    }

    [Fact]
    public async Task Intent_T_Schedule_second_call_is_noop()
    {
        var intent = Intent.Run(() => 1);
        intent.Schedule();
        intent.Schedule();
        Assert.Equal(1, await intent);
    }

    [Fact]
    public void Retry_rejects_non_positive_attempts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Intent.Retry(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicy(-1));
    }

    [Fact]
    public void Timeout_rejects_non_positive_duration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Intent.Timeout(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => Intent.Timeout((-1).Seconds()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeoutPolicy(TimeSpan.FromMilliseconds(-5)));
    }

    [Fact]
    public void Null_arguments_throw()
    {
        Assert.Throws<ArgumentNullException>(() => Intent.Run((Action)null!));
        Assert.Throws<ArgumentNullException>(() => Intent.Run((Func<Task>)null!));
        Assert.Throws<ArgumentNullException>(() => Intent.Defer(null!));
        Assert.Throws<ArgumentNullException>(() => Intent.Run((Func<int>)null!));
        Assert.Throws<ArgumentNullException>(() => Intent.Run((Func<Task<int>>)null!));
        Assert.Throws<ArgumentNullException>(() => Intent.Defer((Func<Intent<int>>)null!));
        Assert.Throws<ArgumentNullException>(() => Intent.Run(() => { }).Configure(null!));
        Assert.Throws<ArgumentNullException>(() => Intent.Run(() => 1).Configure(null!));
    }

    [Fact]
    public async Task AwaitOnCompleted_path_via_non_critical_awaitable()
    {
        async Intent Work()
        {
            await new OnCompletedOnlyAwaitable();
        }

        await Work();
    }

    [Fact]
    public async Task AwaitOnCompleted_path_for_Intent_T()
    {
        async Intent<int> Work()
        {
            await new OnCompletedOnlyAwaitable();
            return 3;
        }

        Assert.Equal(3, await Work());
    }

    [Fact]
    public void MethodBuilder_AwaitOnCompleted_covers_bound_and_unbound_paths()
    {
        var builder = IntentMethodBuilder.Create();
        var sm = new DummyStateMachine();
        var awaiter = new OnCompletedOnlyAwaiter();

        Assert.Throws<InvalidOperationException>(() =>
            builder.AwaitOnCompleted(ref awaiter, ref sm));

        builder.Task.BindStateMachine(sm);
        builder.AwaitOnCompleted(ref awaiter, ref sm);

        var critical = new CriticalAwaiter();
        Assert.Throws<InvalidOperationException>(() =>
        {
            var unbound = IntentMethodBuilder.Create();
            unbound.AwaitUnsafeOnCompleted(ref critical, ref sm);
        });

        builder.AwaitUnsafeOnCompleted(ref critical, ref sm);
    }

    [Fact]
    public void MethodBuilder_T_AwaitOnCompleted_covers_bound_and_unbound_paths()
    {
        var builder = IntentMethodBuilder<int>.Create();
        var sm = new DummyStateMachine();
        var awaiter = new OnCompletedOnlyAwaiter();

        Assert.Throws<InvalidOperationException>(() =>
            builder.AwaitOnCompleted(ref awaiter, ref sm));

        builder.Task.BindStateMachine(sm);
        builder.AwaitOnCompleted(ref awaiter, ref sm);

        var critical = new CriticalAwaiter();
        Assert.Throws<InvalidOperationException>(() =>
        {
            var unbound = IntentMethodBuilder<int>.Create();
            unbound.AwaitUnsafeOnCompleted(ref critical, ref sm);
        });

        builder.AwaitUnsafeOnCompleted(ref critical, ref sm);
    }

    [Fact]
    public void MethodBuilder_SetStateMachine_is_callable()
    {
        var builder = IntentMethodBuilder.Create();
        builder.SetStateMachine(new DummyStateMachine());

        var builderT = IntentMethodBuilder<int>.Create();
        builderT.SetStateMachine(new DummyStateMachine());
        Assert.NotNull(builder.Task);
        Assert.NotNull(builderT.Task);
    }

    [Fact]
    public void MethodBuilder_GetMoveNext_without_machine_throws()
    {
        var builder = IntentMethodBuilder.Create();
        var awaiter = new OnCompletedOnlyAwaiter();
        var sm = new DummyStateMachine();
        Assert.Throws<InvalidOperationException>(() =>
            builder.AwaitOnCompleted(ref awaiter, ref sm));

        var builderT = IntentMethodBuilder<int>.Create();
        Assert.Throws<InvalidOperationException>(() =>
            builderT.AwaitOnCompleted(ref awaiter, ref sm));
    }

    [Fact]
    public async Task Sync_exception_in_async_Intent_propagates()
    {
        async Intent Boom()
        {
            throw new InvalidOperationException("sync-boom");
#pragma warning disable CS0162
            await Task.Yield();
#pragma warning restore CS0162
        }

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Boom());
    }

    [Fact]
    public async Task Sync_exception_in_async_Intent_T_propagates()
    {
        async Intent<int> Boom()
        {
            throw new InvalidOperationException("sync-boom");
#pragma warning disable CS0162
            await Task.Yield();
            return 0;
#pragma warning restore CS0162
        }

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Boom());
    }

    [Fact]
    public async Task OnCompleted_continuation_runs_for_incomplete_Intent()
    {
        var tcs = new TaskCompletionSource();
        Func<Task> body = async () =>
        {
            await tcs.Task;
        };

        var intent = Intent.Run(body);
        var awaiter = intent.GetAwaiter();
        Assert.False(awaiter.IsCompleted);

        var continued = new TaskCompletionSource();
        awaiter.OnCompleted(() => continued.TrySetResult());

        tcs.SetResult();
        await continued.Task;
        await intent;
    }

    [Fact]
    public async Task OnCompleted_continuation_runs_for_incomplete_Intent_T()
    {
        var tcs = new TaskCompletionSource();
        Func<Task<int>> body = async () =>
        {
            await tcs.Task;
            return 11;
        };

        var intent = Intent.Run(body);
        var awaiter = intent.GetAwaiter();
        Assert.False(awaiter.IsCompleted);

        var continued = new TaskCompletionSource();
        awaiter.OnCompleted(() => continued.TrySetResult());

        tcs.SetResult();
        await continued.Task;
        Assert.Equal(11, await intent);
    }

    [Fact]
    public void TimeSpan_extensions()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), 10.Seconds());
        Assert.Equal(TimeSpan.FromMilliseconds(50), 50.Milliseconds());
    }

    [Fact]
    public async Task Intent_T_without_body_faults_on_await()
    {
        var intent = new Intent<int>();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await intent);
    }

    [Fact]
    public void SetResult_with_bound_machine_but_no_smRun_is_safe()
    {
        var intent = new Intent();
        intent.BindStateMachine(new DummyStateMachine());
        intent.SetResult(); // _smRun is null → covers ?. null branch
        Assert.False(intent.IsCompleted);
    }

    [Fact]
    public void Intent_T_SetResult_with_bound_machine_but_no_smRun_is_safe()
    {
        var intent = new Intent<int>();
        intent.BindStateMachine(new DummyStateMachine());
        intent.SetResult(1);
        Assert.False(intent.IsCompleted);
    }

    [Fact]
    public async Task ExecuteStateMachine_without_bound_machine_throws()
    {
        var intent = new Intent();
        intent.BindStateMachine(new DummyStateMachine());
        typeof(Intent)
            .GetField("_template", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(intent, null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await intent);
        Assert.Contains("State machine is not bound", ex.Message);
    }

    [Fact]
    public async Task Intent_T_ExecuteStateMachine_without_bound_machine_throws()
    {
        var intent = new Intent<int>();
        intent.BindStateMachine(new DummyStateMachine());
        typeof(Intent<int>)
            .GetField("_template", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(intent, null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await intent);
        Assert.Contains("State machine is not bound", ex.Message);
    }

    [Fact]
    public async Task SetException_with_bound_machine_and_smRun_faults_inner()
    {
        async Intent Boom()
        {
            await Task.Yield();
            throw new InvalidOperationException("inner");
        }

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Boom());
    }

    [Fact]
    public async Task Retry_does_not_swallow_operation_canceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var policy = new RetryPolicy(3);
        var wrapped = policy.Wrap(_ => Task.FromCanceled(cts.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await wrapped(cts.Token));
    }

    [Fact]
    public void Configure_rejects_null_policies_array_on_Intent_T()
    {
        Assert.Throws<ArgumentNullException>(() => new Intent<int>().Configure(null!));
    }

    private sealed class DummyStateMachine : IAsyncStateMachine
    {
        public void MoveNext() { }
        public void SetStateMachine(IAsyncStateMachine stateMachine) { }
    }

    private readonly struct OnCompletedOnlyAwaitable
    {
        public OnCompletedOnlyAwaiter GetAwaiter() => new();
    }

    private struct OnCompletedOnlyAwaiter : INotifyCompletion
    {
        public bool IsCompleted => false;

        public void OnCompleted(Action continuation) =>
            ThreadPool.QueueUserWorkItem(_ => continuation());

        public void GetResult() { }
    }

    private struct CriticalAwaiter : ICriticalNotifyCompletion
    {
        public bool IsCompleted => false;

        public void OnCompleted(Action continuation) =>
            ThreadPool.QueueUserWorkItem(_ => continuation());

        public void UnsafeOnCompleted(Action continuation) =>
            ThreadPool.QueueUserWorkItem(_ => continuation());

        public void GetResult() { }
    }
}
