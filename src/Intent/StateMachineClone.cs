using System.Reflection;
using System.Runtime.CompilerServices;

namespace Intents;

internal static class StateMachineClone
{
    private static readonly MethodInfo MemberwiseCloneMethod =
        typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>
    /// Copies the boxed compiler state machine while it is still in the initial state (-1).
    /// Each Retry attempt runs MoveNext on a fresh clone; the template is never started.
    /// </summary>
    public static IAsyncStateMachine Clone(IAsyncStateMachine template)
    {
        var clone = (IAsyncStateMachine)MemberwiseCloneMethod.Invoke(template, null)!;
        clone.SetStateMachine(clone);
        return clone;
    }
}
