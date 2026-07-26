using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Intents;

/// <summary>
/// Cold deferred plan that produces a <typeparamref name="T"/> result when awaited.
/// See <see cref="Intent"/> and docs/09-sm-clone-contract.md for Retry / state-machine rules.
/// </summary>
[AsyncMethodBuilder(typeof(IntentMethodBuilder<>))]
public class Intent<T> : IntentPlan
{
    private readonly TaskCompletionSource<T> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private T? _result;
    private bool _hasResult;

    internal Intent() { }

    public bool IsCompleted => _tcs.Task.IsCompleted;

    internal bool HasResult => _hasResult;

    internal void SetResult(T result)
    {
        _result = result;
        _hasResult = true;

        if (HasStateMachineTemplate)
        {
            CompleteStateMachineRun();
            return;
        }
    }

    /// <summary>Used by store policies (Cache / Idempotent) on a hit — does not touch the SM run TCS.</summary>
    internal void ApplyStoredResult(T result)
    {
        _result = result;
        _hasResult = true;
    }

    internal T RequireResult()
    {
        if (!_hasResult)
            throw new InvalidOperationException("Operation body completed without setting a result.");
        return _result!;
    }

    internal void SetException(Exception exception)
    {
        if (TryFaultStateMachine(exception))
            return;

        FaultOuter(exception);
    }

    private void CompleteOuter()
    {
        if (!_hasResult)
            throw new InvalidOperationException("Operation body completed without setting a result.");

        SetLifecycle(IntentLifecycle.Completed);
        _tcs.TrySetResult(_result!);
    }

    private void FaultOuter(Exception exception)
    {
        SetLifecycle(IntentLifecycle.Completed);
        Edi = ExceptionDispatchInfo.Capture(exception);
        _tcs.TrySetException(exception);
    }

    public Intent<T> Configure(params IntentPolicy[] policies)
    {
        ConfigureCore(policies);
        return this;
    }

    /// <summary>
    /// Chains another intent from this result. The source runs when the returned intent is awaited.
    /// </summary>
    public Intent<TResult> Then<TResult>(Func<T, Intent<TResult>> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        var source = this;
        return Intent.From(async () =>
        {
            var value = await source;
            return await next(value);
        });
    }

    public Intent<TResult> Then<TResult>(Func<T, CancellationToken, Task<TResult>> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        var source = this;
        return Intent.From(async ct =>
        {
            var value = await source;
            return await next(value, ct).ConfigureAwait(false);
        });
    }

    public Intent<TResult> Select<TResult>(Func<T, TResult> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var source = this;
        return Intent.From(async () =>
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
        if (!TryBeginSchedule())
            return;

        _ = RunPipelineAsync();
    }

    private async Task RunPipelineAsync()
    {
        SetLifecycle(IntentLifecycle.Running);
        var bridge = new IntentResultBridge<T>(this);
        IntentResultAccess.Current = bridge;
        try
        {
            var body = Body ?? throw new InvalidOperationException("Operation has no body.");
            var pipeline = IntentPipeline.Build(Policies, body);
            await pipeline(CancellationToken.None).ConfigureAwait(false);
            CompleteOuter();
        }
        catch (Exception ex)
        {
            FaultOuter(ex);
        }
        finally
        {
            IntentResultAccess.Current = null;
        }
    }

    internal T GetResult()
    {
        if (Edi is not null)
            Edi.Throw();

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
