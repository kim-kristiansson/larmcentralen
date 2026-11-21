namespace Larmcentralen.Api.Tests;

public class StartupTests
{
    [Theory]
    [InlineData("Security:ApiKey")]
    [InlineData("Security:AdminApiKey")]
    [InlineData("Security:SmsWebhookSecret")]
    public async Task Refuses_to_start_outside_development_without_every_key(string missing)
    {
        await using var factory = new ApiFactory();
        factory.Settings[missing] = "";

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains(missing, Flatten(ex));
    }

    [Fact]
    public async Task Refuses_to_start_when_read_and_admin_key_are_the_same()
    {
        await using var factory = new ApiFactory();
        factory.Settings["Security:AdminApiKey"] = ApiFactory.ReadKey;

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("must be different", Flatten(ex));
    }

    private static string Flatten(Exception ex)
    {
        var messages = new List<string>();
        for (Exception? e = ex; e is not null; e = e.InnerException) messages.Add(e.Message);
        return string.Join(" | ", messages);
    }
}
