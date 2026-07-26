namespace Intents.Tests;

[Collection("Intent")]
public class DeferredExecutionTests
{
    [Fact]
    public async Task Run_does_not_execute_until_awaited()
    {
        var ran = false;
        var intent = Intent.From(() => ran = true);

        Assert.False(ran);
        Assert.Equal(IntentLifecycle.Created, intent.Lifecycle);

        await intent;

        Assert.True(ran);
        Assert.Equal(IntentLifecycle.Completed, intent.Lifecycle);
    }

    [Fact]
    public async Task Async_Intent_method_does_not_run_until_awaited()
    {
        var ran = false;

        async Intent Work()
        {
            ran = true;
            await Task.Yield();
        }

        var intent = Work();
        Assert.False(ran);

        await intent;
        Assert.True(ran);
    }

    [Fact]
    public async Task Await_runs_body_only_once()
    {
        var count = 0;
        var intent = Intent.From(() => count++);

        await intent;
        await intent;

        Assert.Equal(1, count);
    }
}
