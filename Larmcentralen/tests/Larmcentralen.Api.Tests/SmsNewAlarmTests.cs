using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Larmcentralen.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Larmcentralen.Api.Tests;

/// <summary>
/// An alarm SMS for an alarm that isn't documented yet adds it, like the import adds alarms: the shared code, the
/// SMS text as the unit's own text, priority Låg. A missing unit is added only in the avdelning set for SMS-larm.
/// The test numbers are fictional.
/// </summary>
public sealed class SmsNewAlarmTests : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string ElksNumber = "+46766860000";
    private const string Sender = "+46701110000";

    private record Created(int Id);
    private record LastSms(DateTime ReceivedAt, string? From, bool Accepted, string Result);
    private record Settings(LastSms? LastSms, int? AlarmAreaId);
    private record AlarmRow(int Id, string Title, string? AlarmCode, string Severity, string EquipmentTitle, string AreaTitle);
    private record Unit(int Id, string Title, string? Category, int AreaId);

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

    public SmsNewAlarmTests(ApiFactory factory)
    {
        _factory = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Elks:FetchIntervalSeconds", "0");
            b.ConfigureTestServices(s => s.AddSingleton<IAlertSmsSender>(_alerts));
        });
    }

    private HttpClient Admin()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiFactory.AdminKey);
        return client;
    }

    private static string Unique(string prefix) => $"{prefix}{Random.Shared.Next(100000, 999999)}";

    private static async Task<int> PostAsync(HttpClient c, string url, object body)
    {
        using var response = await c.PostAsJsonAsync(url, body, Ct);
        Assert.True(response.IsSuccessStatusCode, $"{url}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return (await response.Content.ReadFromJsonAsync<Created>(Json, Ct))!.Id;
    }

    private static async Task SetAreaAsync(HttpClient c, int? areaId)
    {
        using var put = await c.PutAsJsonAsync("api/settings/sms",
            new { elksNumber = ElksNumber, senderNumbers = new[] { Sender }, alarmAreaId = areaId ?? 0 }, Ct);
        Assert.True(put.IsSuccessStatusCode, await put.Content.ReadAsStringAsync(Ct));
    }

    private async Task SmsAsync(string message)
    {
        using var client = _factory.CreateClient();
        using var response = await client.PostAsync($"api/webhooks/sms?secret={ApiFactory.WebhookSecret}", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["from"] = Sender, ["to"] = ElksNumber, ["message"] = message }), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<string> LastResultAsync(HttpClient c)
        => (await c.GetFromJsonAsync<Settings>("api/settings/sms", Json, Ct))!.LastSms!.Result;

    private static async Task<List<AlarmRow>> AlarmsOnAsync(HttpClient c, int equipmentId)
        => (await c.GetFromJsonAsync<List<AlarmRow>>($"api/alarms?equipmentId={equipmentId}&take=100", Json, Ct))!;

    [Fact]
    public async Task An_undocumented_alarm_on_a_known_unit_is_added_once()
    {
        using var c = Admin();
        await SetAreaAsync(c, null);
        var area = await PostAsync(c, "api/areas", new { title = Unique("SQ ") });
        var title = Unique("Q");
        var unit = await PostAsync(c, "api/equipment", new { title, areaId = area });

        await SmsAsync($"06:56 / T10-T7 Line : {title} : m777.1 Ny givare saknar signal");
        await SmsAsync($"07:10 / T10-T7 Line : {title} : M777.1 Ny givare saknar signal");

        var alarm = Assert.Single(await AlarmsOnAsync(c, unit));
        Assert.Equal("M777.1", alarm.AlarmCode);
        Assert.Equal("Ny givare saknar signal", alarm.Title);
        Assert.Equal("Låg", alarm.Severity);
        Assert.Empty(_alerts.Sent);
        // The second SMS found the alarm the first one added.
        Assert.DoesNotContain("har lagts till", await LastResultAsync(c));
    }

    [Fact]
    public async Task An_unknown_unit_is_not_added_without_an_avdelning_for_SMS_larm()
    {
        using var c = Admin();
        await SetAreaAsync(c, null);
        var title = Unique("Q");

        await SmsAsync($"06:56 / T10-T7 Line : {title} : M777.2 Okänd enhet");

        Assert.Contains("Välj avdelning för SMS-larm", await LastResultAsync(c));
        var units = (await c.GetFromJsonAsync<List<Unit>>("api/equipment", Json, Ct))!;
        Assert.DoesNotContain(units, u => u.Title == title);
    }

    [Fact]
    public async Task An_unknown_unit_is_added_in_the_avdelning_for_SMS_larm_with_its_alarm()
    {
        using var c = Admin();
        var area = await PostAsync(c, "api/areas", new { title = Unique("SQ-linjen ") });
        await SetAreaAsync(c, area);
        try
        {
            var title = Unique("Q");

            await SmsAsync($"06:56 / T10-T7 Line : {title} : M777.3 Tändbrännare ur funktion");

            var unit = Assert.Single((await c.GetFromJsonAsync<List<Unit>>($"api/equipment?areaId={area}", Json, Ct))!);
            Assert.Equal(title, unit.Title);
            Assert.Equal("OCSS", unit.Category);
            var alarm = Assert.Single(await AlarmsOnAsync(c, unit.Id));
            Assert.Equal("M777.3", alarm.AlarmCode);
            Assert.Equal("Tändbrännare ur funktion", alarm.Title);
            Assert.Contains($"Enheten {title} och larmet fanns inte och har lagts till", await LastResultAsync(c));
        }
        finally
        {
            await SetAreaAsync(c, null);
        }
    }

    [Fact]
    public async Task An_avdelning_that_does_not_exist_is_refused()
    {
        using var c = Admin();

        using var put = await c.PutAsJsonAsync("api/settings/sms", new { alarmAreaId = 987654 }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }
}
