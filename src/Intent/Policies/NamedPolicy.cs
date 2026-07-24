namespace Intents;

/// <summary>
/// Sets the ambient operation name used by Trace/Metrics/Cache diagnostics.
/// </summary>
public sealed class NamedPolicy : IntentPolicy
{
    public NamedPolicy(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string Name { get; }
    public int Order => -15;

    public Func<CancellationToken, Task> Wrap(Func<CancellationToken, Task> next)
    {
        var name = Name;
        return async ct =>
        {
            var previous = IntentAmbient.Name;
            IntentAmbient.Name = name;
            try
            {
                await next(ct).ConfigureAwait(false);
            }
            finally
            {
                IntentAmbient.Name = previous;
            }
        };
    }
}
