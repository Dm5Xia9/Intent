namespace Intents.Tests;

[Collection("Intent")]
public class TimeoutPolicyTests
{
    [Fact]
    public async Task Timeout_throws_when_operation_is_slow()
    {
        Func<Task> slow = async () => await Task.Delay(500);
        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await Intent.Run(slow).Configure(Intent.Timeout(50.Milliseconds())));
    }

    [Fact]
    public async Task Timeout_allows_fast_operation()
    {
        Func<Task> fast = async () => await Task.Delay(10);
        await Intent.Run(fast).Configure(Intent.Timeout(2.Seconds()));
    }
}
