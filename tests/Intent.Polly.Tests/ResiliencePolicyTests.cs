using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace Intents.Polly.Tests;

[Collection("Intent.Polly")]
public class ResiliencePolicyTests
{
    public ResiliencePolicyTests()
    {
        IntentPolly.ResetNamedPipelines();
        IntentDiagnostics.Reset();
    }

    [Fact]
    public async Task Before_and_After_run_on_each_Retry_attempt()
    {
        var log = new List<string>();
        var attempts = 0;

        await Intent.From(() =>
            {
                attempts++;
                log.Add($"body-{attempts}");
                if (attempts < 3)
                    throw new InvalidOperationException("fail");
            })
            .WithRetry(3)
            .WithBefore(() => log.Add("before"))
            .WithAfter(() => log.Add("after"));

        Assert.Equal(
            ["before", "body-1", "after", "before", "body-2", "after", "before", "body-3", "after"],
            log);
    }

    [Fact]
    public async Task Retry_succeeds_after_transient_failures()
    {
        var attempts = 0;
        await Intent.From(() =>
            {
                attempts++;
                if (attempts < 3)
                    throw new InvalidOperationException("fail");
            })
            .WithRetry(3);

        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task Retry_exhausts_and_throws_last_exception()
    {
        var attempts = 0;
        Action body = () =>
        {
            attempts++;
            throw new InvalidOperationException($"fail-{attempts}");
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Intent.From(body).WithRetry(3));

        Assert.Equal(3, attempts);
        Assert.Equal("fail-3", ex.Message);
    }

    [Fact]
    public async Task Retry_reexecutes_async_Intent_each_attempt()
    {
        var attempts = 0;

        async Intent Flaky()
        {
            attempts++;
            if (attempts < 3)
                throw new InvalidOperationException("fail");
            await Task.Yield();
        }

        await Flaky().WithRetry(3);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task Retry_with_backoff_delays_between_attempts()
    {
        var attempts = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        await Intent.From(() =>
            {
                attempts++;
                if (attempts < 3)
                    throw new InvalidOperationException("x");
            })
            .WithRetry(3, IntentBackoff.Constant(30.Milliseconds()));

        sw.Stop();
        Assert.Equal(3, attempts);
        Assert.True(sw.ElapsedMilliseconds >= 50, $"elapsed={sw.ElapsedMilliseconds}");
    }

    [Fact]
    public async Task Retry_filter_skips_non_matching_exceptions()
    {
        var attempts = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Intent.From(() =>
                {
                    attempts++;
                    throw new InvalidOperationException("nope");
                })
                .WithRetry(5, shouldRetry: ex => ex is TimeoutException));

        Assert.Equal(1, attempts);
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

        await Intent.From(body)
            .WithRetry(3, attemptTimeout: 40.Milliseconds());

        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Timeout_throws_when_operation_is_slow()
    {
        Func<Task> slow = async () => await Task.Delay(500);
        await Assert.ThrowsAsync<TimeoutRejectedException>(async () =>
            await Intent.From(slow).WithTimeout(50.Milliseconds()));
    }

    [Fact]
    public async Task Timeout_allows_fast_operation()
    {
        Func<Task> fast = async () => await Task.Delay(10);
        await Intent.From(fast).WithTimeout(2.Seconds());
    }

    [Fact]
    public async Task Timeout_wraps_retry_bounding_total_wall_time()
    {
        var attempts = 0;
        Func<Task> body = async () =>
        {
            attempts++;
            await Task.Delay(80);
            throw new InvalidOperationException("fail");
        };

        await Assert.ThrowsAsync<TimeoutRejectedException>(async () =>
            await Intent.From(body).WithRetry(10).WithTimeout(150.Milliseconds()));

        Assert.True(attempts < 10, $"attempts={attempts}");
        Assert.True(attempts >= 1, $"attempts={attempts}");
    }

    [Fact]
    public async Task CircuitBreaker_opens_after_threshold()
    {
        Func<Task> boom = () => throw new InvalidOperationException("x");

        for (var i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await Intent.From(boom).WithCircuitBreaker("cb1", failureThreshold: 3, breakDuration: 5.Seconds()));
        }

        await Assert.ThrowsAsync<BrokenCircuitException>(async () =>
            await Intent.From(boom).WithCircuitBreaker("cb1", failureThreshold: 3, breakDuration: 5.Seconds()));
    }

    [Fact]
    public async Task CircuitBreaker_half_open_probe_can_close()
    {
        Func<Task> boom = () => throw new InvalidOperationException("x");
        var policy = IntentPolly.CircuitBreaker("cb2", failureThreshold: 2, breakDuration: 500.Milliseconds());

        for (var i = 0; i < 2; i++)
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await Intent.From(boom).Configure(policy));

        await Assert.ThrowsAsync<BrokenCircuitException>(async () =>
            await Intent.From(boom).Configure(policy));

        await Task.Delay(550);

        await Intent.From(() => { }).Configure(policy);
        await Intent.From(() => { }).Configure(policy);
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
            await Intent.From(body).WithBulkhead("bh", maxParallelism: 2);
        }

        await Task.WhenAll(Work(), Work(), Work(), Work());
        Assert.Equal(2, max);
    }

    [Fact]
    public async Task WithResilience_uses_custom_pipeline()
    {
        var attempts = 0;
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.Zero
            })
            .Build();

        await Intent.From(() =>
            {
                attempts++;
                if (attempts < 3)
                    throw new InvalidOperationException("x");
            })
            .WithResilience(pipeline);

        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task Combined_policies_normalize_order()
    {
        var attempts = 0;
        await Intent.From(() =>
            {
                attempts++;
                if (attempts < 2)
                    throw new InvalidOperationException("fail");
            })
            .WithAtomic()
            .WithRetry(3)
            .WithTimeout(5.Seconds());

        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Profile_Http_runs()
    {
        await Intent.From(() => { }).Configure(IntentPolly.Http("demo-http"));
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
        }).Configure(IntentPolly.DbWrite("dbw", atomicKey: "dbw"));

        await Intent.WhenAll(Make(), Make(), Make());
        Assert.Equal(1, max);
    }

    [Fact]
    public void Retry_rejects_non_positive_attempts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IntentPolly.Retry(0));
    }

    [Fact]
    public void Timeout_rejects_non_positive_duration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IntentPolly.Timeout(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => IntentPolly.Timeout((-1).Seconds()));
    }

    [Fact]
    public async Task Retry_does_not_swallow_operation_canceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var policy = IntentPolly.Retry(3);
        var wrapped = policy.Wrap(_ => Task.FromCanceled(cts.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await wrapped(cts.Token));
    }

    [Fact]
    public async Task With_star_resilience_chain()
    {
        var log = new List<string>();
        await Intent.From(() => log.Add("body"))
            .WithNamed("void-chain")
            .WithCircuitBreaker("with-void-cb")
            .WithTimeout(5.Seconds())
            .WithRetry(2)
            .WithBulkhead("with-void-bh", 4)
            .WithAtomicOn("with-void-atomic");

        Assert.Contains("body", log);
    }

    [Fact]
    public async Task Retry_reexecutes_async_Intent_T()
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

        Assert.Equal(42, await Flaky().WithRetry(3));
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task FromFactory_recreates_async_Intent_each_attempt()
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

        Assert.Equal(9, await Intent.FromFactory(() => Flaky()).WithRetry(3));
        Assert.Equal(3, attempts);
    }
}
