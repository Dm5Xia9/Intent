namespace Intents.Tests;

[Collection("Intent")]
public class ConfigurePipelineTests
{
    [Fact]
    public async Task Configure_attaches_policies_before_schedule()
    {
        var intent = Intent.Run(() => { }).Configure(Intent.Retry(2));
        Assert.Equal(IntentLifecycle.Configured, intent.Lifecycle);
        await intent;
        Assert.Equal(IntentLifecycle.Completed, intent.Lifecycle);
    }

    [Fact]
    public async Task Configure_after_schedule_throws()
    {
        var intent = Intent.Run(() => { });
        await intent;

        Assert.Throws<InvalidOperationException>(() => intent.Configure(Intent.Retry(1)));
    }
}
