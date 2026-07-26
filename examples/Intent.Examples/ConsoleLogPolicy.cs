using System.Diagnostics;

namespace Intents.Examples;

/// <summary>
/// Example custom policy: logs enter/leave and elapsed time around the rest of the pipeline.
/// Order -9 sits after Trace (-10) and before Activity (-8).
/// </summary>
sealed class ConsoleLogPolicy : IntentPolicy
{
    public int Order => -9;

    public Func<CancellationToken, Task> Wrap(Func<CancellationToken, Task> next) =>
        async ct =>
        {
            Console.WriteLine("  [log] enter");
            var sw = Stopwatch.StartNew();
            try
            {
                await next(ct).ConfigureAwait(false);
                Console.WriteLine($"  [log] ok in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [log] fail after {sw.ElapsedMilliseconds}ms: {ex.GetType().Name}");
                throw;
            }
        };
}
