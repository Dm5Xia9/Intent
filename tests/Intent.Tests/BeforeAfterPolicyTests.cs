namespace Intents.Tests;

[Collection("Intent")]
public class BeforeAfterPolicyTests
{
    [Fact]
    public async Task Before_and_After_run_once_around_body()
    {
        var log = new List<string>();

        await Intent.From(() => log.Add("body"))
            .WithBefore(() => log.Add("before"))
            .WithAfter(() => log.Add("after"));

        Assert.Equal(["before", "body", "after"], log);
    }

    [Fact]
    public async Task Before_and_After_run_on_each_Retry_attempt()
    {
        var log = new List<string>();
        var attempts = 0;

        await Intent.From(() =>
            {
                attempts++;
                log.Add($"body-{attempts}");
                if (attempts < 3)
                    throw new InvalidOperationException("fail");
            })
            .WithRetry(3)
            .WithBefore(() => log.Add("before"))
            .WithAfter(() => log.Add("after"));

        Assert.Equal(
            ["before", "body-1", "after", "before", "body-2", "after", "before", "body-3", "after"],
            log);
    }

    [Fact]
    public async Task After_runs_when_body_faults()
    {
        var after = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Intent.From(() => throw new InvalidOperationException("x"))
                .WithAfter(() => after++));

        Assert.Equal(1, after);
    }

    [Fact]
    public async Task Before_async_overload_receives_token()
    {
        var sawCancel = false;
        using var cts = new CancellationTokenSource();

        await Intent.From(async ct =>
            {
                Assert.True(ct.CanBeCanceled);
                await Task.Yield();
            })
            .WithCancel(cts.Token)
            .WithBefore(async ct =>
                {
                    sawCancel = ct.CanBeCanceled;
                    await Task.Yield();
                });

        Assert.True(sawCancel);
    }
}
