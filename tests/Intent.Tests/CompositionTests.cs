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
            await Intent.Run(body).Useful(Intent.Retry(10), Intent.Timeout(150.Milliseconds())));

        Assert.True(attempts < 10, $"attempts={attempts}");
        Assert.True(attempts >= 1, $"attempts={attempts}");
    }

    [Fact]
    public async Task Combined_policies_normalize_order_regardless_of_Useful_argument_order()
    {
        var attempts = 0;
        await Intent.Run(() =>
            {
                attempts++;
                if (attempts < 2)
                    throw new InvalidOperationException("fail");
            })
            .Useful(Intent.Atomic, Intent.Retry(3), Intent.Timeout(5.Seconds()));

        Assert.Equal(2, attempts);
    }
}
