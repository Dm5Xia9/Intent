namespace Intents;

internal static class IntentAmbient
{
    private static readonly AsyncLocal<string?> NameCurrent = new();
    private static readonly AsyncLocal<Dictionary<string, object?>?> TagsCurrent = new();

    public static string Name
    {
        get => NameCurrent.Value ?? "Intent";
        set => NameCurrent.Value = value;
    }

    public static IReadOnlyDictionary<string, object?> Tags =>
        TagsCurrent.Value ?? EmptyTags.Instance;

    /// <summary>
    /// Merges <paramref name="tags"/> onto a copy of the current tag map. Returns previous map for restore.
    /// </summary>
    public static Dictionary<string, object?>? PushTags(IReadOnlyDictionary<string, object?> tags)
    {
        var previous = TagsCurrent.Value;
        var next = previous is null
            ? new Dictionary<string, object?>(tags, StringComparer.Ordinal)
            : new Dictionary<string, object?>(previous, StringComparer.Ordinal);

        foreach (var (key, value) in tags)
            next[key] = value;

        TagsCurrent.Value = next;
        return previous;
    }

    public static void RestoreTags(Dictionary<string, object?>? previous) =>
        TagsCurrent.Value = previous;

    private static class EmptyTags
    {
        public static readonly IReadOnlyDictionary<string, object?> Instance =
            new Dictionary<string, object?>();
    }
}
