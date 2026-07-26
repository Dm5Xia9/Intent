using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Intents;

/// <summary>
/// Shared cold-plan machinery for <see cref="Intent"/> and <see cref="Intent{T}"/>.
/// Not intended for external subclassing (constructor is assembly-internal).
/// </summary>
public abstract class IntentPlan
{
    private readonly List<IntentPolicy> _policies = [];

    internal IntentPlan()
    {
    }

    private Func<CancellationToken, Task>? _body;
    private IAsyncStateMachine? _template;
    private IAsyncStateMachine? _running;
    private TaskCompletionSource? _smRun;
    private IntentLifecycle _lifecycle = IntentLifecycle.Created;
    private int _scheduleGate;
    private ExceptionDispatchInfo? _edi;

    public IntentLifecycle Lifecycle => _lifecycle;

    protected IReadOnlyList<IntentPolicy> Policies => _policies;

    protected Func<CancellationToken, Task>? Body => _body;

    protected bool HasStateMachineTemplate => _template is not null;

    protected ExceptionDispatchInfo? Edi
    {
        get => _edi;
        set => _edi = value;
    }

    protected void SetLifecycle(IntentLifecycle lifecycle) => _lifecycle = lifecycle;

    internal void SetBody(Func<CancellationToken, Task> body) => _body = body;

    internal void BindStateMachine(IAsyncStateMachine stateMachine)
    {
        _template = stateMachine;
        _body = ExecuteStateMachineAsync;
    }

    internal IAsyncStateMachine? GetBoundStateMachine() => _running ?? _template;

    protected void ConfigureCore(IntentPolicy[] policies)
    {
        ArgumentNullException.ThrowIfNull(policies);
        if (_lifecycle is IntentLifecycle.Scheduled or IntentLifecycle.Running or IntentLifecycle.Completed)
            throw new InvalidOperationException("Cannot configure policies after the operation has been scheduled.");

        _policies.AddRange(policies);
        _lifecycle = IntentLifecycle.Configured;
    }

    /// <summary>Returns false if already scheduled.</summary>
    protected bool TryBeginSchedule()
    {
        if (Interlocked.CompareExchange(ref _scheduleGate, 1, 0) != 0)
            return false;

        _lifecycle = IntentLifecycle.Scheduled;
        return true;
    }

    protected async Task ExecuteStateMachineAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var template = _template ?? throw new InvalidOperationException("State machine is not bound.");
        var previous = IntentAmbient.PushToken(ct);
        try
        {
            var sm = StateMachineClone.Clone(template);
            _running = sm;
            var run = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _smRun = run;
            sm.MoveNext();
            await run.Task.ConfigureAwait(false);
        }
        finally
        {
            IntentAmbient.RestoreToken(previous);
        }
    }

    protected void CompleteStateMachineRun() => _smRun?.TrySetResult();

    /// <summary>True if the exception was routed to the in-flight SM run TCS.</summary>
    protected bool TryFaultStateMachine(Exception exception)
    {
        if (_template is not null && _smRun is not null && !_smRun.Task.IsCompleted)
        {
            _smRun.TrySetException(exception);
            return true;
        }

        return false;
    }
}
