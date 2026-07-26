namespace Intents;

/// <summary>
/// Attaches ambient key/value tags for the duration of the operation (Activity, Trace, Metrics).
/// </summary>
public sealed class TagPolicy : IntentPolicy
{
    private readonly Dictionary<string, object?> _tags;

    public TagPolicy(IEnumerable<KeyValuePair<string, object?>> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        _tags = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in tags)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            _tags[key] = value;
        }

        if (_tags.Count == 0)
            throw new ArgumentException("At least one tag is required.", nameof(tags));
    }

    public IReadOnlyDictionary<string, object?> Tags => _tags;

    /// <summary>Just inside Named so tags see the operation name scope.</summary>
    public int Order => IntentPipelineOrder.Tag;

    public Func<CancellationToken, Task> Wrap(Func<CancellationToken, Task> next)
    {
        var added = _tags;
        return async ct =>
        {
            var snapshot = IntentAmbient.PushTags(added);
            try
            {
                await next(ct).ConfigureAwait(false);
            }
            finally
            {
                IntentAmbient.RestoreTags(snapshot);
            }
        };
    }
}
