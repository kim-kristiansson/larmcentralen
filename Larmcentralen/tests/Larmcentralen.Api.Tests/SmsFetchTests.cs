using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Larmcentralen.Api.Services;
using Larmcentralen.Application.DTOs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Larmcentralen.Api.Tests;

/// <summary>
/// Fetching alarm SMS from 46elks' SMS history, so the API needs no public address: the first fetch only marks
/// where the history ends, later fetches handle what came in since, once each, also when the webhook got it too.
/// The test numbers are fictional.
/// </summary>
public sealed class SmsFetchTests : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string ElksNumber = "+46766860000";
    private const string Sender = "+46701110000";

    private record Created(int Id);
    private record LastSms(DateTime ReceivedAt, string? From, bool Accepted, string Result);
    private record LastFetch(DateTime At, string? Error);
    private record Settings(string? ElksNumber, List<string> SenderNumbers, LastSms? LastSms, DateTime? UpdatedAt, LastFetch? LastFetch,
        string? ElksUsername, bool HasElksPassword, bool ServerHasElksAccount);
    private record ConnectionTest(bool Ok, List<string> Messages);
    private record Problem(string? Detail);

    private sealed class FakeAlertSender : IAlertSmsSender
    {
        public ConcurrentQueue<string> Sent { get; } = new();

        public Task<bool> SendAsync(string message, CancellationToken ct = default)
        {
            Sent.Enqueue(message);
            return Task.FromResult(true);
        }
    }

    private sealed class FakeElks : IElksApi
    {
        public List<ElksSms> Messages { get; } = [];
        public List<ElksNumber> Numbers { get; } = [];
        public Exception? Failure { get; set; }
        public List<DateTime> AskedAfter { get; } = [];
        public List<ElksLogin> Logins { get; } = [];

        public Task<List<ElksSms>> GetIncomingAsync(ElksLogin login, string to, DateTime after, int maxPages, CancellationToken ct)
        {
            Logins.Add(login);
            AskedAfter.Add(after);
            if (Failure is not null) throw Failure;
            return Task.FromResult(Messages.Where(m => m.To == to && m.Created > after).OrderBy(m => m.Created).ToList());
        }

        public Task<List<ElksNumber>> GetNumbersAsync(ElksLogin login, CancellationToken ct)
        {
            Logins.Add(login);
            if (Failure is not null) throw Failure;
            return Task.FromResult(Numbers.ToList());
        }

        public Task<string?> SendSmsAsync(ElksLogin login, string from, string to, string message, CancellationToken ct)
            => throw new InvalidOperationException("Tests never send through 46elks.");
    }

    private readonly FakeAlertSender _alerts = new();
    private readonly FakeElks _history = new();
    private readonly WebApplicationFactory<Program> _factory;

    public SmsFetchTests(ApiFactory factory)
    {
        _factory = factory.WithWebHostBuilder(b =>
        {
            // The test calls FetchOnceAsync itself; no background fetching alongside it.
            b.UseSetting("Email:FetchIntervalSeconds", "0");
            // The server's own (test) 46elks login, used until one is saved under SMS-larm.
            b.UseSetting("Email:ElksUsername", "server-test-user");
            b.UseSetting("Email:ElksPassword", "server-test-password");
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<IAlertSmsSender>(_alerts);
                s.AddSingleton<IElksApi>(_history);
            });
        });
    }

    private HttpClient Admin()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiFactory.AdminKey);
        return client;
    }

    private Task<int> FetchAsync() => _factory.Services.GetRequiredService<ElksSmsFetcher>().FetchOnceAsync(Ct);

    private static async Task<int> PostAsync(HttpClient c, string url, object body)
    {
        using var response = await c.PostAsJsonAsync(url, body, Ct);
        Assert.True(response.IsSuccessStatusCode, $"{url}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return (await response.Content.ReadFromJsonAsync<Created>(Json, Ct))!.Id;
    }

    private static async Task<Settings> GetSettingsAsync(HttpClient c)
        => (await c.GetFromJsonAsync<Settings>("api/settings/sms", Json, Ct))!;

    private static ElksSms Sms(string id, DateTime created, string message, string from = Sender, string to = ElksNumber)
        => new(id, created, from, to, message);

    [Fact]
    public async Task New_SMS_are_fetched_and_handled_once_and_old_ones_never()
    {
        using var c = Admin();
        var area = await PostAsync(c, "api/areas", new { title = "SQ-linjen" });
        var unit = await PostAsync(c, "api/equipment", new { title = "T102", areaId = area });
        await PostAsync(c, "api/alarms", new { title = "Ingen svarssignal tändbrännare", alarmCode = "M206.6", severity = "Kritisk", equipmentId = unit });
        using (var put = await c.PutAsJsonAsync("api/settings/sms", new { elksNumber = ElksNumber, senderNumbers = new[] { Sender } }, Ct))
            Assert.True(put.IsSuccessStatusCode);

        // Already in the history before fetching started: never acted on.
        var start = DateTime.UtcNow;
        _history.Messages.Add(Sms("s-old", start.AddHours(-2), "05:00 / T10-T7 Line : T102 : M206.6 Gammalt larm"));
        Assert.Equal(0, await FetchAsync());
        Assert.Empty(_alerts.Sent);

        // Came in since: handled, oldest first, and SMS to other numbers are left out by the history.
        _history.Messages.Add(Sms("s-new", start.AddMinutes(1), "06:56 / T10-T7 Line : T102 : M206.6 Ingen svarssignal ytterlucka"));
        _history.Messages.Add(Sms("s-stranger", start.AddMinutes(2), "Hej", from: "+46709990000"));
        _history.Messages.Add(Sms("s-other-number", start.AddMinutes(3), "06:57 / T10-T7 Line : T102 : M206.6", to: "+46766869999"));
        Assert.Equal(2, await FetchAsync());

        var alert = Assert.Single(_alerts.Sent);
        Assert.Contains("Kritisk: M206.6 på T102", alert);
        Assert.Contains("ytterlucka", alert);
        var settings = await GetSettingsAsync(c);
        Assert.False(settings.LastSms!.Accepted); // the stranger came last
        Assert.Null(settings.LastFetch!.Error);

        // Nothing new: nothing handled again.
        Assert.Equal(0, await FetchAsync());
        Assert.Equal(start.AddMinutes(2), _history.AskedAfter[^1], TimeSpan.FromMilliseconds(1));

        // The same SMS through the webhook as well (46elks id "s-new"): not a second alert.
        using var client = _factory.CreateClient();
        using var webhook = await client.PostAsync($"api/webhooks/sms?secret={ApiFactory.WebhookSecret}", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["id"] = "s-new", ["from"] = Sender, ["to"] = ElksNumber, ["message"] = "06:56 / T10-T7 Line : T102 : M206.6 Ingen svarssignal" }), Ct);
        Assert.Equal(HttpStatusCode.OK, webhook.StatusCode);
        Assert.Single(_alerts.Sent);
    }

    [Fact]
    public async Task A_failed_fetch_is_shown_on_the_admin_page()
    {
        using var c = Admin();
        using (var put = await c.PutAsJsonAsync("api/settings/sms", new { elksNumber = ElksNumber, senderNumbers = new[] { Sender } }, Ct))
            Assert.True(put.IsSuccessStatusCode);
        _history.Failure = new ElksException("46elks godkände inte inloggningen (Email:ElksUsername, Email:ElksPassword).");

        Assert.Equal(-1, await FetchAsync());

        Assert.Contains("godkände inte inloggningen", (await GetSettingsAsync(c)).LastFetch!.Error);
    }

    [Fact]
    public void The_history_times_are_read_as_UTC()
    {
        var created = ElksApi.ParseCreated("2018-07-13T13:57:23.741000");

        Assert.Equal(new DateTime(2018, 7, 13, 13, 57, 23, 741, DateTimeKind.Utc), created);
        Assert.Equal(DateTimeKind.Utc, created!.Value.Kind);
    }

    [Fact]
    public async Task A_saved_46elks_login_is_write_only_and_used_instead_of_the_servers()
    {
        using var c = Admin();
        using (var put = await c.PutAsJsonAsync("api/settings/sms", new { elksNumber = ElksNumber, senderNumbers = new[] { Sender } }, Ct))
            Assert.True(put.IsSuccessStatusCode);
        Assert.True((await GetSettingsAsync(c)).ServerHasElksAccount);

        // A username alone is not enough.
        using (var half = await c.PutAsJsonAsync("api/settings/sms", new { elksUsername = "u-company" }, Ct))
        {
            Assert.Equal(HttpStatusCode.BadRequest, half.StatusCode);
            Assert.Contains("lösenordet", (await half.Content.ReadFromJsonAsync<Problem>(Json, Ct))!.Detail);
        }

        using var saved = await c.PutAsJsonAsync("api/settings/sms", new { elksUsername = " u-company ", elksPassword = "company-test-password" }, Ct);
        Assert.True(saved.IsSuccessStatusCode);
        var body = await saved.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("company-test-password", body);
        var settings = await GetSettingsAsync(c);
        Assert.Equal("u-company", settings.ElksUsername);
        Assert.True(settings.HasElksPassword);

        // Saving the numbers again keeps the login.
        using (var again = await c.PutAsJsonAsync("api/settings/sms", new { senderNumbers = new[] { Sender } }, Ct))
            Assert.True(again.IsSuccessStatusCode);

        _history.Numbers.Add(new ElksNumber(ElksNumber, true, ["sms", "voice"], null));
        using var test = await c.PostAsync("api/settings/sms/test", null, Ct);
        var result = (await test.Content.ReadFromJsonAsync<ConnectionTest>(Json, Ct))!;
        Assert.True(result.Ok, string.Join(" ", result.Messages));
        Assert.Equal(new ElksLogin("u-company", "company-test-password"), _history.Logins[^1]);

        // Removing it falls back to the server's login.
        using (var removed = await c.PutAsJsonAsync("api/settings/sms", new { elksUsername = "" }, Ct))
            Assert.True(removed.IsSuccessStatusCode);
        Assert.False((await GetSettingsAsync(c)).HasElksPassword);
        using (await c.PostAsync("api/settings/sms/test", null, Ct)) { }
        Assert.Equal("server-test-user", _history.Logins[^1].Username);
    }

    [Fact]
    public async Task The_connection_test_says_when_the_number_is_not_on_the_account()
    {
        using var c = Admin();
        using (var put = await c.PutAsJsonAsync("api/settings/sms", new { elksNumber = ElksNumber, senderNumbers = new[] { Sender } }, Ct))
            Assert.True(put.IsSuccessStatusCode);
        _history.Numbers.Add(new ElksNumber("+46766869999", true, ["sms"], null));

        using var test = await c.PostAsync("api/settings/sms/test", null, Ct);
        var result = (await test.Content.ReadFromJsonAsync<ConnectionTest>(Json, Ct))!;

        Assert.False(result.Ok);
        Assert.Contains(result.Messages, m => m.Contains($"{ElksNumber} finns inte på kontot") && m.Contains("+46766869999"));
    }

    [Fact]
    public async Task The_connection_test_is_admin_only()
    {
        using var reader = _factory.CreateClient();
        reader.DefaultRequestHeaders.Add("X-Api-Key", ApiFactory.ReadKey);

        using var test = await reader.PostAsync("api/settings/sms/test", null, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, test.StatusCode);
    }
}
