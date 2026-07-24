namespace Intents.Tests;

[Collection("Intent")]
public class AtomicPolicyTests
{
    [Fact]
    public async Task Atomic_prevents_interleaving()
    {
        var inCritical = 0;
        var maxInCritical = 0;
        var gate = new object();

        async Task Work()
        {
            Func<Task> body = async () =>
            {
                var now = Interlocked.Increment(ref inCritical);
                lock (gate)
                    maxInCritical = Math.Max(maxInCritical, now);

                await Task.Delay(30);
                Interlocked.Decrement(ref inCritical);
            };

            await Intent.Run(body).Useful(Intent.Atomic);
        }

        await Task.WhenAll(Work(), Work(), Work());

        Assert.Equal(1, maxInCritical);
    }

    [Fact]
    public async Task Atomically_runs_action_under_atomic_policy()
    {
        var ran = false;
        await Intent.Atomically(() => ran = true);
        Assert.True(ran);
    }
}
