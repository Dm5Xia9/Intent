namespace Intents.Tests;

[Collection("Intent")]
public class UsefulPipelineTests
{
    [Fact]
    public async Task Useful_attaches_policies_before_schedule()
    {
        var intent = Intent.Run(() => { }).Useful(Intent.Retry(2));
        Assert.Equal(IntentLifecycle.Configured, intent.Lifecycle);
        await intent;
        Assert.Equal(IntentLifecycle.Completed, intent.Lifecycle);
    }

    [Fact]
    public async Task Useful_after_schedule_throws()
    {
        var intent = Intent.Run(() => { });
        await intent;

        Assert.Throws<InvalidOperationException>(() => intent.Useful(Intent.Retry(1)));
    }
}
