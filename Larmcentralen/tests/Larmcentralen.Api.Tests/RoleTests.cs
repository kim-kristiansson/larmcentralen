using System.Net;
using System.Net.Http.Json;
using Larmcentralen.Api.Controllers;
using Larmcentralen.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Larmcentralen.Api.Tests;

public sealed class RoleTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task Missing_or_wrong_key_is_401(string? key)
    {
        using var client = factory.ClientWithKey(key);

        var response = await client.GetAsync("api/sync", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("api/sync")]
    [InlineData("api/alarms?search=temp")]
    [InlineData("api/areas")]
    [InlineData("api/equipment")]
    public async Task Read_key_can_read_and_search(string url)
    {
        using var client = factory.ClientWithKey(ApiFactory.ReadKey);

        var response = await client.GetAsync(url, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("POST", "api/alarms")]
    [InlineData("PUT", "api/alarms/1")]
    [InlineData("DELETE", "api/alarms/1")]
    [InlineData("POST", "api/solutions")]
    [InlineData("PUT", "api/solutions/1")]
    [InlineData("DELETE", "api/solutions/1")]
    [InlineData("POST", "api/areas")]
    [InlineData("DELETE", "api/areas/1")]
    [InlineData("POST", "api/equipment")]
    [InlineData("DELETE", "api/equipment/1")]
    [InlineData("POST", "api/alarms/1/upload-sharepoint")]
    [InlineData("DELETE", "api/alarms/1/solutions/1")]
    [InlineData("POST", "api/alarms/1/solutions/1/detach")]
    public async Task Read_key_cannot_change_anything(string method, string url)
    {
        using var client = factory.ClientWithKey(ApiFactory.ReadKey);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url)
        {
            Content = JsonContent.Create(new { })
        }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_key_can_change_things()
    {
        using var client = factory.ClientWithKey(ApiFactory.AdminKey);

        // Gets past authorization to the controller, which says the alarm doesn't exist.
        var response = await client.DeleteAsync("api/alarms/999999", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(ApiFactory.ReadKey, "User", false)]
    [InlineData(ApiFactory.AdminKey, "Admin", true)]
    public async Task Me_tells_the_app_what_its_key_allows(string key, string role, bool canEdit)
    {
        using var client = factory.ClientWithKey(key);

        var access = await client.GetFromJsonAsync<AccessDto>("api/me", Ct);

        Assert.Equal(new AccessDto(role, canEdit), access);
    }

    [Fact]
    public async Task Sms_webhook_needs_its_secret_not_an_api_key()
    {
        using var client = factory.ClientWithKey(ApiFactory.AdminKey);
        var form = () => new FormUrlEncodedContent(new Dictionary<string, string>());

        var withoutSecret = await client.PostAsync("api/webhooks/sms", form(), Ct);
        var wrongSecret = await client.PostAsync("api/webhooks/sms?secret=nope", form(), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, withoutSecret.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongSecret.StatusCode);
    }

    [Fact]
    public async Task Sms_webhook_with_secret_gets_past_the_check()
    {
        using var client = factory.ClientWithKey(null);

        // A message that isn't an alarm SMS: accepted and ignored, so nothing is sent to 46elks.
        var response = await client.PostAsync($"api/webhooks/sms?secret={ApiFactory.WebhookSecret}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["from"] = "+46700000000", ["message"] = "hej" }), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Every_endpoint_that_changes_data_requires_admin()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;
        var writes = endpoints
            .Where(e => e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Any(m => m is not ("GET" or "HEAD")) ?? true)
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .ToList();

        Assert.NotEmpty(writes);
        Assert.All(writes, e => Assert.Contains(
            e.Metadata.GetOrderedMetadata<IAuthorizeData>(), a => a.Policy == Policies.Edit));
    }
}
