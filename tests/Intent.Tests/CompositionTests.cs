namespace Intents.Tests;

[Collection("Intent")]
public class CompositionTests
{
    [Fact]
    public async Task Timeout_wraps_retry_bounding_total_wall_time()
    {
        var attempts = 0;
        Func<Task> body = async () =>
        {
            attempts++;
            await Task.Delay(80);
            throw new InvalidOperationException("fail");
        };

        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await Intent.From(body).WithRetry(10).WithTimeout(150.Milliseconds()));

        Assert.True(attempts < 10, $"attempts={attempts}");
        Assert.True(attempts >= 1, $"attempts={attempts}");
    }

    [Fact]
    public async Task Combined_policies_normalize_order_regardless_of_Configure_argument_order()
    {
        var attempts = 0;
        await Intent.From(() =>
            {
                attempts++;
                if (attempts < 2)
                    throw new InvalidOperationException("fail");
            })
            .WithAtomic()
            .WithRetry(3)
            .WithTimeout(5.Seconds());

        Assert.Equal(2, attempts);
    }
}
