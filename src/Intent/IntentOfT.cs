using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Intents;

[AsyncMethodBuilder(typeof(IntentMethodBuilder<>))]
public class Intent<T>
{
    private readonly List<IntentPolicy> _policies = [];
    private readonly TaskCompletionSource<T> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Func<CancellationToken, Task>? _body;
    private IAsyncStateMachine? _template;
    private IAsyncStateMachine? _running;
    private TaskCompletionSource? _smRun;
    private IntentLifecycle _lifecycle = IntentLifecycle.Created;
    private int _scheduleGate;
    private ExceptionDispatchInfo? _edi;
    private T? _result;
    private bool _hasResult;

    internal Intent() { }

    public IntentLifecycle Lifecycle => _lifecycle;

    public bool IsCompleted => _tcs.Task.IsCompleted;

    internal void SetBody(Func<CancellationToken, Task> body) => _body = body;

    internal void BindStateMachine(IAsyncStateMachine stateMachine)
    {
        _template = stateMachine;
        _body = ExecuteStateMachineAsync;
    }

    internal IAsyncStateMachine? GetBoundStateMachine() => _running ?? _template;

    private async Task ExecuteStateMachineAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var template = _template ?? throw new InvalidOperationException("State machine is not bound.");
        var sm = StateMachineClone.Clone(template);
        _running = sm;
        var run = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _smRun = run;
        sm.MoveNext();
        await run.Task.ConfigureAwait(false);
    }

    internal void SetResult(T result)
    {
        _result = result;
        _hasResult = true;

        if (_template is not null)
        {
            _smRun?.TrySetResult();
            return;
        }
    }

    internal void SetException(Exception exception)
    {
        if (_template is not null && _smRun is not null && !_smRun.Task.IsCompleted)
        {
            _smRun.TrySetException(exception);
            return;
        }

        FaultOuter(exception);
    }

    private void CompleteOuter()
    {
        if (!_hasResult)
            throw new InvalidOperationException("Operation body completed without setting a result.");

        _lifecycle = IntentLifecycle.Completed;
        _tcs.TrySetResult(_result!);
    }

    private void FaultOuter(Exception exception)
    {
        _lifecycle = IntentLifecycle.Completed;
        _edi = ExceptionDispatchInfo.Capture(exception);
        _tcs.TrySetException(exception);
    }

    public Intent<T> Configure(params IntentPolicy[] policies)
    {
        ArgumentNullException.ThrowIfNull(policies);
        if (_lifecycle is IntentLifecycle.Scheduled or IntentLifecycle.Running or IntentLifecycle.Completed)
            throw new InvalidOperationException("Cannot configure policies after the operation has been scheduled.");

        _policies.AddRange(policies);
        _lifecycle = IntentLifecycle.Configured;
        return this;
    }

    /// <summary>
    /// Chains another intent from this result. The source runs when the returned intent is awaited.
    /// </summary>
    public Intent<TResult> Then<TResult>(Func<T, Intent<TResult>> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        var source = this;
        return Intent.Run(async () =>
        {
            var value = await source;
            return await next(value);
        });
    }

    public Intent<TResult> Then<TResult>(Func<T, CancellationToken, Task<TResult>> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        var source = this;
        return Intent.Run(async ct =>
        {
            var value = await source;
            return await next(value, ct).ConfigureAwait(false);
        });
    }

    public Intent<TResult> Select<TResult>(Func<T, TResult> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var source = this;
        return Intent.Run(async () =>
        {
            var value = await source;
            return map(value);
        });
    }

    public IntentAwaiter<T> GetAwaiter()
    {
        Schedule();
        return new IntentAwaiter<T>(this);
    }

    internal void Schedule()
    {
        if (Interlocked.CompareExchange(ref _scheduleGate, 1, 0) != 0)
            return;

        _lifecycle = IntentLifecycle.Scheduled;
        _ = RunPipelineAsync();
    }

    private async Task RunPipelineAsync()
    {
        _lifecycle = IntentLifecycle.Running;
        try
        {
            var idem = _policies.OfType<IdempotentPolicy>().LastOrDefault();
            if (idem is not null)
            {
                var (_, value) = await IntentIdempotencyStore.RunResultAsync(
                    idem.Key,
                    idem.Ttl,
                    ExecuteBodyAsync,
                    CancellationToken.None).ConfigureAwait(false);
                _result = value;
                _hasResult = true;
                CompleteOuter();
                return;
            }

            _result = await ExecuteBodyAsync(CancellationToken.None).ConfigureAwait(false);
            _hasResult = true;
            CompleteOuter();
        }
        catch (Exception ex)
        {
            FaultOuter(ex);
        }
    }

    private async Task<T> ExecuteBodyAsync(CancellationToken ct)
    {
        var cache = _policies.OfType<CachePolicy>().LastOrDefault();
        if (cache is not null && IntentCacheStore.TryGet<T>(cache.Key, out var cached))
            return cached;

        var body = _body ?? throw new InvalidOperationException("Operation has no body.");
        var pipeline = IntentPipeline.Build(_policies, body);
        await pipeline(ct).ConfigureAwait(false);

        if (!_hasResult)
            throw new InvalidOperationException("Operation body completed without setting a result.");

        if (cache is not null)
            IntentCacheStore.Set(cache.Key, _result!, cache.Ttl);

        return _result!;
    }

    internal T GetResult()
    {
        if (_edi is not null)
            _edi.Throw();

        return _tcs.Task.GetAwaiter().GetResult();
    }

    internal void OnCompleted(Action continuation) =>
        _tcs.Task.ConfigureAwait(false).GetAwaiter().OnCompleted(continuation);

    internal void UnsafeOnCompleted(Action continuation) =>
        _tcs.Task.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(continuation);
}

public readonly struct IntentAwaiter<T> : ICriticalNotifyCompletion
{
    private readonly Intent<T> _intent;

    internal IntentAwaiter(Intent<T> intent) => _intent = intent;

    public bool IsCompleted => _intent.IsCompleted;

    public T GetResult() => _intent.GetResult();

    public void OnCompleted(Action continuation) => _intent.OnCompleted(continuation);

    public void UnsafeOnCompleted(Action continuation) => _intent.UnsafeOnCompleted(continuation);
}
