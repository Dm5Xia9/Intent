using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Intents;

[AsyncMethodBuilder(typeof(IntentMethodBuilder))]
public class Intent
{
    private readonly List<IntentPolicy> _policies = [];
    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Func<CancellationToken, Task>? _body;
    private IAsyncStateMachine? _template;
    private IAsyncStateMachine? _running;
    private TaskCompletionSource? _smRun;
    private IntentLifecycle _lifecycle = IntentLifecycle.Created;
    private int _scheduleGate;
    private ExceptionDispatchInfo? _edi;

    internal Intent() { }

    public IntentLifecycle Lifecycle => _lifecycle;

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
    /// Named <c>Atomically</c> because <see cref="IntentPolicies.Atomic"/> is the policy used with <c>Configure</c> / <c>WithAtomic</c>.
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

    internal void SetResult()
    {
        if (_template is not null)
        {
            _smRun?.TrySetResult();
            return;
        }

        CompleteOuter();
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
        _lifecycle = IntentLifecycle.Completed;
        _tcs.TrySetResult();
    }

    private void FaultOuter(Exception exception)
    {
        _lifecycle = IntentLifecycle.Completed;
        _edi = ExceptionDispatchInfo.Capture(exception);
        _tcs.TrySetException(exception);
    }

    public Intent Configure(params IntentPolicy[] policies)
    {
        ArgumentNullException.ThrowIfNull(policies);
        if (_lifecycle is IntentLifecycle.Scheduled or IntentLifecycle.Running or IntentLifecycle.Completed)
            throw new InvalidOperationException("Cannot configure policies after the operation has been scheduled.");

        _policies.AddRange(policies);
        _lifecycle = IntentLifecycle.Configured;
        return this;
    }

    public IntentAwaiter GetAwaiter()
    {
        Schedule();
        return new IntentAwaiter(this);
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
            var body = _body ?? throw new InvalidOperationException("Operation has no body.");
            var pipeline = IntentPipeline.Build(_policies, body);
            var idem = _policies.OfType<IdempotentPolicy>().LastOrDefault();
            if (idem is not null)
                await IntentIdempotencyStore.RunVoidAsync(idem.Key, idem.Ttl, pipeline, CancellationToken.None)
                    .ConfigureAwait(false);
            else
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
        if (_edi is not null)
            _edi.Throw();

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

public static class TimeSpanExtensions
{
    public static TimeSpan Seconds(this int value) => TimeSpan.FromSeconds(value);

    public static TimeSpan Milliseconds(this int value) => TimeSpan.FromMilliseconds(value);
}
