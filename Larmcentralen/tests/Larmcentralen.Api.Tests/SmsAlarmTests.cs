using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Larmcentralen.Api.Services;
using Larmcentralen.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Larmcentralen.Api.Tests;

/// <summary>
/// Incoming alarm SMS: admins set the 46elks number and the alarm system's sender numbers (any Swedish way of
/// writing them), and the webhook only acts on SMS from those senders to that number.
/// The test numbers are fictional.
/// </summary>
public sealed class SmsAlarmTests : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string ElksNumber = "+46766860000";
    private const string Sender = "+46701110000";

    private record Created(int Id);
    private record Problem(string? Detail);
    private record LastSms(DateTime ReceivedAt, string? From, bool Accepted, string Result);
    private record Settings(string? ElksNumber, List<string> SenderNumbers, LastSms? LastSms, DateTime? UpdatedAt);

    private sealed class FakeAlertSender : IAlertSmsSender
    {
        public ConcurrentQueue<string> Sent { get; } = new();

        public Task<bool> SendAsync(string message, CancellationToken ct = default)
        {
            Sent.Enqueue(message);
            return Task.FromResult(true);
        }
    }

    private readonly FakeAlertSender _alerts = new();
    private readonly WebApplicationFactory<Program> _factory;

    public SmsAlarmTests(ApiFactory factory)
    {
        _factory = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IAlertSmsSender>(_alerts)));
    }

    private HttpClient Client(string? key)
    {
        var client = _factory.CreateClient();
        if (key is not null) client.DefaultRequestHeaders.Add("X-Api-Key", key);
        return client;
    }

    private HttpClient Admin() => Client(ApiFactory.AdminKey);

    private static string Unique(string prefix) => $"{prefix}{Random.Shared.Next(100000, 999999)}";

    private static async Task<int> PostAsync(HttpClient c, string url, object body)
    {
        using var response = await c.PostAsJsonAsync(url, body, Ct);
        Assert.True(response.IsSuccessStatusCode, $"{url}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return (await response.Content.ReadFromJsonAsync<Created>(Json, Ct))!.Id;
    }

    private static async Task<Settings> PutSettingsAsync(HttpClient c, object body)
    {
        using var response = await c.PutAsJsonAsync("api/settings/sms", body, Ct);
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return (await response.Content.ReadFromJsonAsync<Settings>(Json, Ct))!;
    }

    private static async Task<Settings> GetSettingsAsync(HttpClient c)
        => (await c.GetFromJsonAsync<Settings>("api/settings/sms", Json, Ct))!;

    /// <summary>Posts the SMS the way 46elks does: a form, with the secret in the query string.</summary>
    private async Task<HttpResponseMessage> SmsAsync(string from, string to, string message, string secret = ApiFactory.WebhookSecret)
    {
        using var client = Client(null);
        return await client.PostAsync($"api/webhooks/sms?secret={secret}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["from"] = from, ["to"] = to, ["message"] = message, ["direction"] = "incoming", ["id"] = "s" + Guid.NewGuid().ToString("N"),
        }), Ct);
    }

    /// <summary>A unit with a Hög alarm M206.6 and a Låg alarm M200.0.</summary>
    private static async Task<string> SeedUnitAsync(HttpClient c)
    {
        var area = await PostAsync(c, "api/areas", new { title = Unique("SQ ") });
        var unit = Unique("Q");
        var equipment = await PostAsync(c, "api/equipment", new { title = unit, areaId = area });
        await PostAsync(c, "api/alarms", new { title = "Ingen svarssignal tändbrännare", alarmCode = "M206.6", severity = "Hög", equipmentId = equipment });
        await PostAsync(c, "api/alarms", new { title = "Batteribortfall PLC", alarmCode = "M200.0", severity = "Låg", equipmentId = equipment });
        return unit;
    }

    [Theory]
    [InlineData("0701234567", "+46701234567")]
    [InlineData("070-123 45 67", "+46701234567")]
    [InlineData("+46 70 123 45 67", "+46701234567")]
    [InlineData("+46 (0)70-123 45 67", "+46701234567")]
    [InlineData("0046701234567", "+46701234567")]
    [InlineData("46701234567", "+46701234567")]
    [InlineData("+4607 01234567", "+46701234567")]
    [InlineData("0766-86 10 00", "+46766861000")]
    [InlineData("08-123 456 78", "+46812345678")]
    [InlineData("0370-123 45", "+4637012345")]
    public void Swedish_numbers_are_normalised(string typed, string expected)
        => Assert.Equal(expected, SwedishPhoneNumber.Normalise(typed));

    [Theory]
    [InlineData("")]
    [InlineData("OCSS")]
    [InlineData("12345")]
    [InlineData("+4512345678")]
    [InlineData("070123456789")]
    [InlineData("+46")]
    public void Other_numbers_are_not_Swedish(string typed) => Assert.Null(SwedishPhoneNumber.Normalise(typed));

    [Fact]
    public async Task Settings_are_admin_only()
    {
        using var reader = Client(ApiFactory.ReadKey);

        using var get = await reader.GetAsync("api/settings/sms", Ct);
        using var put = await reader.PutAsJsonAsync("api/settings/sms", new { elksNumber = ElksNumber }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
    }

    [Fact]
    public async Task Numbers_are_saved_normalised_without_duplicates()
    {
        using var c = Admin();

        var saved = await PutSettingsAsync(c, new { elksNumber = "0766-86 00 00", senderNumbers = new[] { "070-111 00 00", "+46701110000", " ", "0702220000" } });

        Assert.Equal(ElksNumber, saved.ElksNumber);
        Assert.Equal(["+46701110000", "+46702220000"], saved.SenderNumbers);
        Assert.Equal(saved.SenderNumbers, (await GetSettingsAsync(c)).SenderNumbers);
    }

    [Fact]
    public async Task Leaving_a_field_out_keeps_it_and_empty_clears_it()
    {
        using var c = Admin();
        await PutSettingsAsync(c, new { elksNumber = ElksNumber, senderNumbers = new[] { Sender } });

        var kept = await PutSettingsAsync(c, new { senderNumbers = new[] { "0702220000" } });
        Assert.Equal(ElksNumber, kept.ElksNumber);

        var cleared = await PutSettingsAsync(c, new { elksNumber = "" });
        Assert.Null(cleared.ElksNumber);
        Assert.Equal(["+46702220000"], cleared.SenderNumbers);
    }

    [Theory]
    [InlineData("12345", null)]
    [InlineData(null, "OCSS")]
    public async Task A_number_that_is_not_Swedish_is_refused_with_a_reason(string? elks, string? sender)
    {
        using var c = Admin();
        await PutSettingsAsync(c, new { elksNumber = ElksNumber, senderNumbers = new[] { Sender } });

        using var response = await c.PutAsJsonAsync("api/settings/sms",
            new { elksNumber = elks, senderNumbers = sender is null ? null : new[] { sender } }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Problem>(Json, Ct);
        Assert.Contains("inte ett svenskt telefonnummer", problem!.Detail);
        var unchanged = await GetSettingsAsync(c);
        Assert.Equal(ElksNumber, unchanged.ElksNumber);
        Assert.Equal([Sender], unchanged.SenderNumbers);
    }

    [Fact]
    public async Task The_46elks_number_cannot_also_be_a_sender()
    {
        using var c = Admin();

        using var response = await c.PutAsJsonAsync("api/settings/sms", new { elksNumber = ElksNumber, senderNumbers = new[] { "0766860000" } }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_serious_alarm_from_the_alarm_system_sends_an_alert()
    {
        using var c = Admin();
        var unit = await SeedUnitAsync(c);
        await PutSettingsAsync(c, new { elksNumber = ElksNumber, senderNumbers = new[] { "070-111 00 00" } });

        using var response = await SmsAsync(Sender, ElksNumber, $"06:56 / T10-T7 Line : {unit} : m206.6 Ingen svarssignal tändbrännare ytterlucka");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // 46elks would send any text in the response back to the alarm system as a reply SMS.
        Assert.Equal("", await response.Content.ReadAsStringAsync(Ct));
        var alert = Assert.Single(_alerts.Sent);
        Assert.Contains($"Hög: M206.6 på {unit}", alert);
        Assert.Contains("ytterlucka", alert);
        var last = (await GetSettingsAsync(c)).LastSms!;
        Assert.True(last.Accepted);
        Assert.Equal(Sender, last.From);
        Assert.Contains("Avisering skickad", last.Result);
    }

    [Fact]
    public async Task A_minor_alarm_is_received_without_an_alert()
    {
        using var c = Admin();
        var unit = await SeedUnitAsync(c);
        await PutSettingsAsync(c, new { elksNumber = ElksNumber, senderNumbers = new[] { Sender } });

        using var response = await SmsAsync(Sender, ElksNumber, $"07:01 / T10-T7 Line : {unit} : M200.0 Batteribortfall PLC");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_alerts.Sent);
        var last = (await GetSettingsAsync(c)).LastSms!;
        Assert.True(last.Accepted);
        Assert.Contains("Ingen avisering", last.Result);
    }

    [Theory]
    [InlineData("+46709990000", ElksNumber, "inte en godkänd avsändare")]
    [InlineData("OCSS", ElksNumber, "inte en godkänd avsändare")]
    [InlineData(Sender, "+46766869999", "inte till 46elks-numret")]
    public async Task An_SMS_from_another_sender_or_to_another_number_is_turned_away(string from, string to, string reason)
    {
        using var c = Admin();
        var unit = await SeedUnitAsync(c);
        await PutSettingsAsync(c, new { elksNumber = ElksNumber, senderNumbers = new[] { Sender } });

        using var response = await SmsAsync(from, to, $"06:56 / T10-T7 Line : {unit} : M206.6 Ingen svarssignal");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync(Ct));
        Assert.Empty(_alerts.Sent);
        var last = (await GetSettingsAsync(c)).LastSms!;
        Assert.False(last.Accepted);
        Assert.Contains(reason, last.Result);
    }

    [Fact]
    public async Task Every_SMS_is_turned_away_until_the_numbers_are_set()
    {
        using var c = Admin();
        var unit = await SeedUnitAsync(c);
        await PutSettingsAsync(c, new { elksNumber = "", senderNumbers = Array.Empty<string>() });

        using var response = await SmsAsync(Sender, ElksNumber, $"06:56 / T10-T7 Line : {unit} : M206.6 Ingen svarssignal");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_alerts.Sent);
        Assert.False((await GetSettingsAsync(c)).LastSms!.Accepted);
    }

    [Fact]
    public async Task The_webhook_needs_its_secret()
    {
        using var response = await SmsAsync(Sender, ElksNumber, "06:56 / T10-T7 Line : T102 : M206.6", secret: "wrong");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_SMS_that_is_not_an_alarm_is_received_and_explained()
    {
        using var c = Admin();
        await PutSettingsAsync(c, new { elksNumber = ElksNumber, senderNumbers = new[] { Sender } });

        using var response = await SmsAsync(Sender, ElksNumber, "Test från OCSS");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_alerts.Sent);
        Assert.Contains("känns inte igen", (await GetSettingsAsync(c)).LastSms!.Result);
    }
}
