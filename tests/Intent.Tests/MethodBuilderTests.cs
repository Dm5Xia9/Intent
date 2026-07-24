namespace Intents.Tests;

[Collection("Intent")]
public class MethodBuilderTests
{
    [Fact]
    public async Task Async_Intent_T_returns_value()
    {
        async Intent<int> Compute()
        {
            await Task.Yield();
            return 42;
        }

        var intent = Compute();
        Assert.Equal(IntentLifecycle.Created, intent.Lifecycle);

        var result = await intent;
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task Async_Intent_propagates_exception()
    {
        async Intent Boom()
        {
            await Task.Yield();
            throw new InvalidOperationException("boom");
        }

        var intent = Boom();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await intent);
        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public async Task Async_Intent_with_inner_await()
    {
        async Intent Work()
        {
            await Task.Delay(10);
        }

        await Work();
    }
}
