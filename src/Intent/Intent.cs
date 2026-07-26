using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Intents;

[AsyncMethodBuilder(typeof(IntentMethodBuilder))]
public class Intent : IntentPlan
{
    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Intent() { }

    /// <summary>
    /// Cancellation linked by Cancel / Timeout policies for the currently executing intent body.
    /// Prefer <c>From(async ct =&gt; …)</c> when possible; use this inside <c>async Intent</c> methods
    /// that have no <see cref="CancellationToken"/> parameter. See docs/09-sm-clone-contract.md.
    /// </summary>
    public static CancellationToken CurrentCancellationToken => IntentAmbient.Token;

    public bool IsCompleted => _tcs.Task.IsCompleted;

    public static Intent From(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var intent = new Intent();
        intent.SetBody(ct =>
        {
            ct.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        });
        return intent;
    }

    public static Intent From(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var intent = new Intent();
        intent.SetBody(async _ => await action().ConfigureAwait(false));
        return intent;
    }

    public static Intent From(Func<CancellationToken, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var intent = new Intent();
        intent.SetBody(async ct => await action(ct).ConfigureAwait(false));
        return intent;
    }

    /// <summary>
    /// Creates an operation that invokes <paramref name="factory"/> when executed.
    /// Useful when the factory itself has side effects beyond a single Intent body.
    /// </summary>
    public static Intent FromFactory(Func<Intent> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var intent = new Intent();
        intent.SetBody(async _ =>
        {
            var inner = factory();
            await inner;
        });
        return intent;
    }

    public static Intent<T> From<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        var intent = new Intent<T>();
        intent.SetBody(ct =>
        {
            ct.ThrowIfCancellationRequested();
            intent.SetResult(func());
            return Task.CompletedTask;
        });
        return intent;
    }

    public static Intent<T> From<T>(Func<Task<T>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        var intent = new Intent<T>();
        intent.SetBody(async _ =>
        {
            var result = await func().ConfigureAwait(false);
            intent.SetResult(result);
        });
        return intent;
    }

    public static Intent<T> From<T>(Func<CancellationToken, Task<T>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        var intent = new Intent<T>();
        intent.SetBody(async ct =>
        {
            var result = await func(ct).ConfigureAwait(false);
            intent.SetResult(result);
        });
        return intent;
    }

    public static Intent<T> FromFactory<T>(Func<Intent<T>> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var intent = new Intent<T>();
        intent.SetBody(async _ =>
        {
            var inner = factory();
            var result = await inner;
            intent.SetResult(result);
        });
        return intent;
    }

    /// <summary>
    /// Runs <paramref name="action"/> under <see cref="IntentPolicies.Atomic"/>.
    /// </summary>
    public static Intent Atomically(Action action) => From(action).Configure(IntentPolicies.Atomic);

    public static Intent Atomically(Func<Task> action) => From(action).Configure(IntentPolicies.Atomic);

    public static Intent Atomically(string key, Action action) => From(action).Configure(IntentPolicies.AtomicOn(key));

    public static Intent Atomically(string key, Func<Task> action) => From(action).Configure(IntentPolicies.AtomicOn(key));

    /// <summary>
    /// Runs all intents concurrently (each is scheduled on await of the composite).
    /// Policies on the returned Intent wrap the whole WhenAll, not each child.
    /// </summary>
    public static Intent WhenAll(params Intent[] intents)
    {
        ArgumentNullException.ThrowIfNull(intents);
        if (intents.Length == 0)
            return From(() => { });

        return From(async () =>
        {
            var tasks = new Task[intents.Length];
            for (var i = 0; i < intents.Length; i++)
                tasks[i] = AwaitAsTask(intents[i]);
            await Task.WhenAll(tasks).ConfigureAwait(false);
        });
    }

    /// <summary>
    /// Runs intents one after another in order.
    /// </summary>
    public static Intent Sequence(params Intent[] intents)
    {
        ArgumentNullException.ThrowIfNull(intents);
        return From(async () =>
        {
            foreach (var intent in intents)
                await intent;
        });
    }

    /// <summary>
    /// Schedules the intent without awaiting. Faults are emitted to <see cref="IntentDiagnostics"/>.
    /// </summary>
    public static void Background(Intent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        _ = RunBackgroundAsync(intent);
    }

    private static async Task RunBackgroundAsync(Intent intent)
    {
        try
        {
            await intent;
        }
        catch (Exception ex)
        {
            IntentDiagnostics.EmitTrace(new IntentTraceEvent(
                IntentAmbient.Name, IntentTracePhase.Faulted, null, ex));
        }
    }

    private static async Task AwaitAsTask(Intent intent) => await intent;

    internal void SetResult()
    {
        if (HasStateMachineTemplate)
        {
            CompleteStateMachineRun();
            return;
        }

        CompleteOuter();
    }

    internal void SetException(Exception exception)
    {
        if (TryFaultStateMachine(exception))
            return;

        FaultOuter(exception);
    }

    private void CompleteOuter()
    {
        SetLifecycle(IntentLifecycle.Completed);
        _tcs.TrySetResult();
    }

    private void FaultOuter(Exception exception)
    {
        SetLifecycle(IntentLifecycle.Completed);
        Edi = ExceptionDispatchInfo.Capture(exception);
        _tcs.TrySetException(exception);
    }

    public Intent Configure(params IntentPolicy[] policies)
    {
        ConfigureCore(policies);
        return this;
    }

    public IntentAwaiter GetAwaiter()
    {
        Schedule();
        return new IntentAwaiter(this);
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
    }

    internal void GetResult()
    {
        if (Edi is not null)
            Edi.Throw();

        _tcs.Task.GetAwaiter().GetResult();
    }

    internal void OnCompleted(Action continuation) =>
        _tcs.Task.ConfigureAwait(false).GetAwaiter().OnCompleted(continuation);

    internal void UnsafeOnCompleted(Action continuation) =>
        _tcs.Task.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(continuation);
}

public readonly struct IntentAwaiter : ICriticalNotifyCompletion
{
    private readonly Intent _intent;

    internal IntentAwaiter(Intent intent) => _intent = intent;

    public bool IsCompleted => _intent.IsCompleted;

    public void GetResult() => _intent.GetResult();

    public void OnCompleted(Action continuation) => _intent.OnCompleted(continuation);

    public void UnsafeOnCompleted(Action continuation) => _intent.UnsafeOnCompleted(continuation);
}
