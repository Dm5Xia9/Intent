using System.Runtime.CompilerServices;

namespace Intents;

public struct IntentMethodBuilder
{
    private Intent _intent;

    public static IntentMethodBuilder Create() => new() { _intent = new Intent() };

    public Intent Task => _intent;

    public void Start<TStateMachine>(ref TStateMachine stateMachine)
        where TStateMachine : IAsyncStateMachine
    {
        IAsyncStateMachine boxed = stateMachine;
        boxed.SetStateMachine(boxed);
        _intent.BindStateMachine(boxed);
    }

    public void SetStateMachine(IAsyncStateMachine stateMachine)
    {
    }

    public void SetResult() => _intent.SetResult();

    public void SetException(Exception exception) => _intent.SetException(exception);

    public void AwaitOnCompleted<TAwaiter, TStateMachine>(
        ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : INotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        var continuation = GetMoveNextAction();
        awaiter.OnCompleted(continuation);
    }

    public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
        ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : ICriticalNotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        var continuation = GetMoveNextAction();
        awaiter.UnsafeOnCompleted(continuation);
    }

    private Action GetMoveNextAction()
    {
        var boxed = _intent.GetBoundStateMachine()
            ?? throw new InvalidOperationException("State machine is not bound.");
        return boxed.MoveNext;
    }
}

public struct IntentMethodBuilder<T>
{
    private Intent<T> _intent;

    public static IntentMethodBuilder<T> Create() => new() { _intent = new Intent<T>() };

    public Intent<T> Task => _intent;

    public void Start<TStateMachine>(ref TStateMachine stateMachine)
        where TStateMachine : IAsyncStateMachine
    {
        IAsyncStateMachine boxed = stateMachine;
        boxed.SetStateMachine(boxed);
        _intent.BindStateMachine(boxed);
    }

    public void SetStateMachine(IAsyncStateMachine stateMachine)
    {
    }

    public void SetResult(T result) => _intent.SetResult(result);

    public void SetException(Exception exception) => _intent.SetException(exception);

    public void AwaitOnCompleted<TAwaiter, TStateMachine>(
        ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : INotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        var continuation = GetMoveNextAction();
        awaiter.OnCompleted(continuation);
    }

    public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
        ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : ICriticalNotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        var continuation = GetMoveNextAction();
        awaiter.UnsafeOnCompleted(continuation);
    }

    private Action GetMoveNextAction()
    {
        var boxed = _intent.GetBoundStateMachine()
            ?? throw new InvalidOperationException("State machine is not bound.");
        return boxed.MoveNext;
    }
}
