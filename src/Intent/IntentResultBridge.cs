namespace Intents;

/// <summary>
/// Bridge so store policies (<see cref="IdempotentPolicy"/>, <see cref="CachePolicy"/>) can
/// short-circuit or persist <see cref="Intent{T}"/> results entirely inside <see cref="IntentPolicy.Wrap"/>.
/// </summary>
internal interface IIntentResultBridge
{
    Type ResultType { get; }
    bool HasResult { get; }
    void SetStoredResult(object? value);
    object? CaptureResultAfterBody();
}

internal static class IntentResultAccess
{
    private static readonly AsyncLocal<IIntentResultBridge?> CurrentLocal = new();

    public static IIntentResultBridge? Current
    {
        get => CurrentLocal.Value;
        set => CurrentLocal.Value = value;
    }
}

internal sealed class IntentResultBridge<T> : IIntentResultBridge
{
    private readonly Intent<T> _owner;

    public IntentResultBridge(Intent<T> owner) => _owner = owner;

    public Type ResultType => typeof(T);

    public bool HasResult => _owner.HasResult;

    public void SetStoredResult(object? value) => _owner.ApplyStoredResult((T)value!);

    public object? CaptureResultAfterBody() => _owner.RequireResult();
}
