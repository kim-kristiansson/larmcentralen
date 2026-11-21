using Larmcentralen.Client.Core.Api;
using Microsoft.Extensions.DependencyInjection;

namespace Larmcentralen.Client.Core.Tests;

public class ApiKeyTests
{
    [Fact]
    public void Configured_key_is_sent_on_every_request()
    {
        var http = BuildClient(o => o.ApiKey = "s3cret");

        Assert.True(http.DefaultRequestHeaders.TryGetValues(ServiceCollectionExtensions.ApiKeyHeader, out var values));
        Assert.Equal(["s3cret"], values);
    }

    [Fact]
    public void No_key_sends_no_header()
    {
        var http = BuildClient(o => o.ApiKey = " ");

        Assert.False(http.DefaultRequestHeaders.Contains(ServiceCollectionExtensions.ApiKeyHeader));
    }

    private static HttpClient BuildClient(Action<ApiClientOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLarmcentralenClient(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.db"), configure);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IHttpClientFactory>().CreateClient("LarmApi");
    }
}
