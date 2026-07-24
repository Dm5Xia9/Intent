namespace Intents;

internal static class IntentPipeline
{
    public static Func<CancellationToken, Task> Build(
        IReadOnlyList<IntentPolicy> policies,
        Func<CancellationToken, Task> body)
    {
        var ordered = policies.OrderBy(p => p.Order).ToArray();
        var next = body;
        for (var i = ordered.Length - 1; i >= 0; i--)
            next = ordered[i].Wrap(next);
        return next;
    }
}
