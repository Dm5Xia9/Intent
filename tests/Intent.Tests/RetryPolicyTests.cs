namespace Intents.Tests;

[Collection("Intent")]
public class RetryPolicyTests
{
    [Fact]
    public async Task Retry_succeeds_after_transient_failures()
    {
        var attempts = 0;
        await Intent.From(() =>
            {
                attempts++;
                if (attempts < 3)
                    throw new InvalidOperationException("fail");
            })
            .WithRetry(3);

        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task Retry_exhausts_and_throws_last_exception()
    {
        var attempts = 0;
        Action body = () =>
        {
            attempts++;
            throw new InvalidOperationException($"fail-{attempts}");
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Intent.From(body).WithRetry(3));

        Assert.Equal(3, attempts);
        Assert.Equal("fail-3", ex.Message);
    }

    [Fact]
    public async Task Retry_reexecutes_async_Intent_each_attempt()
    {
        var attempts = 0;

        async Intent Flaky()
        {
            attempts++;
            if (attempts < 3)
                throw new InvalidOperationException("fail");
            await Task.Yield();
        }

        await Flaky().WithRetry(3);
        Assert.Equal(3, attempts);
    }
}
