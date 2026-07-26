using Intents.Polly;
using Polly.Timeout;

namespace Intents.Tests;

[Collection("Intent")]
public class ContractEdgeCaseTests
{
    public ContractEdgeCaseTests()
    {
        IntentCacheStore.Clear();
        IntentIdempotencyStore.Clear();
        IntentDiagnostics.Reset();
    }

    [Fact]
    public async Task Retry_preserves_method_parameter_captures()
    {
        var seen = new List<string>();

        async Intent Charge(string paymentId)
        {
            seen.Add(paymentId);
            if (seen.Count < 3)
                throw new InvalidOperationException("transient");
            await Task.Yield();
        }

        await Charge("pay-42").WithRetry(3);
        Assert.Equal(["pay-42", "pay-42", "pay-42"], seen);
    }

    [Fact]
    public async Task Retry_shared_mutable_capture_is_visible_across_attempts()
    {
        var log = new List<string>();

        async Intent Bad()
        {
            log.Add("x");
            if (log.Count < 2)
                throw new InvalidOperationException("fail");
            await Task.Yield();
        }

        await Bad().WithRetry(3);
        Assert.Equal(["x", "x"], log);
    }

    [Fact]
    public async Task Nested_await_Intent_child_policies_are_independent()
    {
        var childRuns = 0;
        var parentRuns = 0;

        async Intent Child()
        {
            childRuns++;
            await Task.Yield();
        }

        async Intent Parent()
        {
            parentRuns++;
            await Child().WithNamed("child");
        }

        await Parent().WithNamed("parent");
        Assert.Equal(1, parentRuns);
        Assert.Equal(1, childRuns);
    }

    [Fact]
    public async Task Nested_await_already_completed_child_does_not_rerun_on_parent_retry()
    {
        var childRuns = 0;
        var parentAttempts = 0;

        async Intent Child()
        {
            childRuns++;
            await Task.Yield();
        }

        // Child created once outside — completed after first parent attempt
        var child = Child();

        async Intent Parent()
        {
            parentAttempts++;
            await child;
            if (parentAttempts < 2)
                throw new InvalidOperationException("retry parent");
        }

        await Parent().WithRetry(3);
        Assert.Equal(2, parentAttempts);
        Assert.Equal(1, childRuns);
    }

    [Fact]
    public async Task Double_await_does_not_rerun_body()
    {
        var runs = 0;
        var intent = Intent.From(() => runs++);

        await intent;
        await intent;

        Assert.Equal(1, runs);
        Assert.Equal(IntentLifecycle.Completed, intent.Lifecycle);
    }

    [Fact]
    public async Task Double_await_Intent_T_returns_same_result()
    {
        var runs = 0;
        var intent = Intent.From(() =>
        {
            runs++;
            return 7;
        });

        Assert.Equal(7, await intent);
        Assert.Equal(7, await intent);
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task Configure_after_schedule_throws_on_void_and_T()
    {
        var voidIntent = Intent.From(() => { });
        await voidIntent;
        Assert.Throws<InvalidOperationException>(() => voidIntent.WithNamed("late"));

        var typed = Intent.From(() => 1);
        await typed;
        Assert.Throws<InvalidOperationException>(() => typed.WithNamed("late"));
    }

    [Fact]
    public async Task Timeout_cancels_token_aware_body()
    {
        var sawCancel = false;
        Func<CancellationToken, Task> body = async ct =>
        {
            try
            {
                await Task.Delay(5.Seconds(), ct);
            }
            catch (OperationCanceledException)
            {
                sawCancel = true;
                throw;
            }
        };

        await Assert.ThrowsAsync<TimeoutRejectedException>(async () =>
            await Intent.From(body).WithTimeout(40.Milliseconds()));

        Assert.True(sawCancel);
    }

    [Fact]
    public async Task Timeout_with_side_effect_before_await_still_applies_effect()
    {
        var sideEffects = 0;
        Func<CancellationToken, Task> body = async ct =>
        {
            Interlocked.Increment(ref sideEffects);
            await Task.Delay(5.Seconds(), ct);
        };

        await Assert.ThrowsAsync<TimeoutRejectedException>(async () =>
            await Intent.From(body).WithTimeout(40.Milliseconds()));

        Assert.Equal(1, sideEffects);
    }

    [Fact]
    public async Task From_async_ct_receives_cancel_policy_token()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;

        var run = Intent.From(async ct =>
        {
            seen = ct;
            await Task.Yield();
        }).WithCancel(cts.Token);

        await run;
        Assert.True(seen.CanBeCanceled);
    }

    [Fact]
    public async Task Async_Intent_observes_CurrentCancellationToken_from_Cancel()
    {
        using var cts = new CancellationTokenSource();
        var saw = false;

        async Intent Work()
        {
            saw = Intent.CurrentCancellationToken.CanBeCanceled;
            await Task.Yield();
        }

        await Work().WithCancel(cts.Token);
        Assert.True(saw);
    }

    [Fact]
    public async Task Idempotent_and_Cache_work_via_Wrap_only()
    {
        var calls = 0;
        Func<int> body = () =>
        {
            calls++;
            return 11;
        };

        Assert.Equal(11, await Intent.From(body).WithIdempotent("wrap-idem", 5.Seconds()));
        Assert.Equal(11, await Intent.From(body).WithIdempotent("wrap-idem", 5.Seconds()));
        Assert.Equal(1, calls);

        calls = 0;
        Assert.Equal(5, await Intent.From(() => { calls++; return 5; }).WithCache("wrap-cache", 5.Seconds()));
        Assert.Equal(5, await Intent.From(() => { calls++; return 99; }).WithCache("wrap-cache", 5.Seconds()));
        Assert.Equal(1, calls);
    }
}
