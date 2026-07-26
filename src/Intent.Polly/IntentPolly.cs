using System.Threading.RateLimiting;
using Intents.Time;
using Polly;
using Polly.CircuitBreaker;
using Polly.Registry;
using Polly.Retry;
using Polly.Timeout;

namespace Intents.Polly;

/// <summary>
/// Factories for Polly-backed resilience <see cref="IntentPolicy"/> instances.
/// </summary>
public static class IntentPolly
{
    private static ResiliencePipelineRegistry<string> _circuitRegistry = new();
    private static ResiliencePipelineRegistry<string> _bulkheadRegistry = new();

    /// <summary>Pipeline order: CircuitBreaker (outermost of resilience band).</summary>
    public const int CircuitBreakerOrder = IntentPipelineOrder.CircuitBreaker;

    /// <summary>Pipeline order: overall Timeout.</summary>
    public const int TimeoutOrder = IntentPipelineOrder.Timeout;

    /// <summary>Pipeline order: Retry.</summary>
    public const int RetryOrder = IntentPipelineOrder.Retry;

    /// <summary>Pipeline order: Bulkhead / concurrency limiter.</summary>
    public const int BulkheadOrder = IntentPipelineOrder.Bulkhead;

    public static IntentPolicy Resilience(ResiliencePipeline pipeline, int order = 0) =>
        new PollyPolicy(pipeline, order);

    /// <summary>
    /// Wall-clock timeout: linked CTS cancels the body (cooperative), and
    /// <see cref="Task.WaitAsync(TimeSpan, CancellationToken)"/> bounds the wait.
    /// Throws Polly's <see cref="TimeoutRejectedException"/>. Bodies that ignore cancellation
    /// may keep running in the background after the wait returns — see docs.
    /// </summary>
    public static IntentPolicy Timeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");

        return new CallbackPolicy(TimeoutOrder, next => async ct =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(timeout);
            var previous = IntentAmbient.PushToken(linked.Token);
            try
            {
                await next(linked.Token).WaitAsync(timeout, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                throw new TimeoutRejectedException(timeout);
            }
            catch (TimeoutException)
            {
                throw new TimeoutRejectedException(timeout);
            }
            finally
            {
                IntentAmbient.RestoreToken(previous);
            }
        });
    }

    public static IntentPolicy Retry(
        int attempts,
        Func<int, TimeSpan>? backoff = null,
        Func<Exception, bool>? shouldRetry = null,
        TimeSpan? attemptTimeout = null)
    {
        if (attempts < 1)
            throw new ArgumentOutOfRangeException(nameof(attempts), "Attempts must be at least 1.");
        if (attemptTimeout is { } t && t <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(attemptTimeout), "Attempt timeout must be positive.");

        var predicate = shouldRetry ?? (static ex => ex is not OperationCanceledException);
        var options = new RetryStrategyOptions
        {
            MaxRetryAttempts = attempts - 1,
            ShouldHandle = args =>
            {
                if (args.Outcome.Exception is not { } ex)
                    return PredicateResult.False();
                return predicate(ex) ? PredicateResult.True() : PredicateResult.False();
            }
        };

        if (backoff is not null)
        {
            options.DelayGenerator = args =>
            {
                // AttemptNumber is 0-based for the first retry; IntentBackoff uses 1 = first retry.
                var delay = backoff(args.AttemptNumber + 1);
                return new ValueTask<TimeSpan?>(delay);
            };
        }
        else
        {
            options.Delay = TimeSpan.Zero;
        }

        var retryPipeline = new ResiliencePipelineBuilder().AddRetry(options).Build();
        if (attemptTimeout is not { } at)
            return new PollyPolicy(retryPipeline, RetryOrder);

        return new CallbackPolicy(RetryOrder, next => async ct =>
        {
            await retryPipeline.ExecuteAsync(
                static async (state, token) =>
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
                    linked.CancelAfter(state.Timeout);
                    var previous = IntentAmbient.PushToken(linked.Token);
                    try
                    {
                        await state.Next(linked.Token).WaitAsync(state.Timeout, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (linked.IsCancellationRequested && !token.IsCancellationRequested)
                    {
                        throw new TimeoutRejectedException(state.Timeout);
                    }
                    catch (TimeoutException)
                    {
                        throw new TimeoutRejectedException(state.Timeout);
                    }
                    finally
                    {
                        IntentAmbient.RestoreToken(previous);
                    }
                },
                (Next: next, Timeout: at),
                ct).ConfigureAwait(false);
        });
    }

    public static IntentPolicy CircuitBreaker(
        string name,
        int failureThreshold = 5,
        TimeSpan? breakDuration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (failureThreshold < 1)
            throw new ArgumentOutOfRangeException(nameof(failureThreshold));

        var duration = breakDuration ?? TimeSpan.FromSeconds(30);
        if (duration < TimeSpan.FromMilliseconds(500))
            throw new ArgumentOutOfRangeException(nameof(breakDuration), "Break duration must be at least 500ms (Polly requirement).");
        if (duration > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(breakDuration));

        var pipeline = _circuitRegistry.GetOrAddPipeline(name, builder =>
        {
            builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 1.0,
                MinimumThroughput = failureThreshold,
                SamplingDuration = TimeSpan.FromMinutes(1),
                BreakDuration = duration,
                ShouldHandle = new PredicateBuilder().Handle<Exception>(static ex => ex is not OperationCanceledException)
            });
        });

        return new PollyPolicy(pipeline, CircuitBreakerOrder);
    }

    /// <summary>
    /// Limits concurrency for a named pool via Polly <c>AddConcurrencyLimiter</c> (bulkhead).
    /// </summary>
    public static IntentPolicy Bulkhead(string name, int maxParallelism)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (maxParallelism < 1)
            throw new ArgumentOutOfRangeException(nameof(maxParallelism));

        var pipeline = _bulkheadRegistry.GetOrAddPipeline(name, builder =>
        {
            builder.AddConcurrencyLimiter(new ConcurrencyLimiterOptions
            {
                PermitLimit = maxParallelism,
                QueueLimit = int.MaxValue
            });
        });

        return new PollyPolicy(pipeline, BulkheadOrder);
    }

    /// <summary>
    /// Named + Activity + Metrics + CircuitBreaker + Bulkhead + Retry(+attemptTimeout) + Timeout.
    /// </summary>
    public static IntentPolicy[] Http(
        string name = "http",
        int retryAttempts = 3,
        TimeSpan? timeout = null,
        TimeSpan? attemptTimeout = null,
        int failureThreshold = 5,
        TimeSpan? breakDuration = null,
        int maxParallelism = 32) =>
    [
        IntentPolicies.Named(name),
        IntentPolicies.Activity,
        IntentPolicies.Metrics,
        CircuitBreaker(name, failureThreshold, breakDuration),
        Bulkhead(name, maxParallelism),
        Retry(
            retryAttempts,
            IntentBackoff.Exponential(100.Milliseconds()),
            attemptTimeout: attemptTimeout ?? 2.Seconds()),
        Timeout(timeout ?? 10.Seconds())
    ];

    /// <summary>
    /// Named + Activity + AtomicOn + Retry.
    /// </summary>
    public static IntentPolicy[] DbWrite(
        string name = "db-write",
        string? atomicKey = null,
        int retryAttempts = 3) =>
    [
        IntentPolicies.Named(name),
        IntentPolicies.Activity,
        IntentPolicies.AtomicOn(atomicKey ?? name),
        Retry(retryAttempts)
    ];

    /// <summary>Clears named circuit and bulkhead registries (for tests).</summary>
    public static void ResetNamedPipelines()
    {
        var oldCircuits = Interlocked.Exchange(ref _circuitRegistry, new ResiliencePipelineRegistry<string>());
        var oldBulkheads = Interlocked.Exchange(ref _bulkheadRegistry, new ResiliencePipelineRegistry<string>());
        oldCircuits.Dispose();
        oldBulkheads.Dispose();
    }
}

public static class IntentBackoff
{
    /// <summary>
    /// Backoff before attempt <paramref name="attempt"/> (1 = first retry after failure).
    /// </summary>
    public static Func<int, TimeSpan> Exponential(
        TimeSpan initial,
        double factor = 2.0,
        TimeSpan? max = null)
    {
        if (initial < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(initial));
        if (factor < 1.0)
            throw new ArgumentOutOfRangeException(nameof(factor));

        var maxDelay = max ?? TimeSpan.FromMinutes(1);
        return attempt =>
        {
            var ms = initial.TotalMilliseconds * Math.Pow(factor, attempt - 1);
            var delay = TimeSpan.FromMilliseconds(ms);
            return delay > maxDelay ? maxDelay : delay;
        };
    }

    public static Func<int, TimeSpan> Constant(TimeSpan delay) => _ => delay;
}

file sealed class CallbackPolicy : IntentPolicy
{
    private readonly Func<Func<CancellationToken, Task>, Func<CancellationToken, Task>> _factory;

    public CallbackPolicy(int order, Func<Func<CancellationToken, Task>, Func<CancellationToken, Task>> factory)
    {
        Order = order;
        _factory = factory;
    }

    public int Order { get; }

    public Func<CancellationToken, Task> Wrap(Func<CancellationToken, Task> next) => _factory(next);
}
