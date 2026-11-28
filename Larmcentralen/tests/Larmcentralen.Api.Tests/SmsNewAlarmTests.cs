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
/// SMS text as the unit's own text, priority Låg; a new unit goes beside the other OCSS units. An SMS that isn't an
/// alarm is kept word for word.
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
    private record Settings(LastSms? LastSms);
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


    private record Area(int Id, string Title);
    private record Unknown(int Id, DateTime ReceivedAt, string? From, string Message);

    /// <summary>
    /// The numbers, and this class's avdelning with the alarm system's units (category OCSS). Its database is its
    /// own, so no other test puts OCSS units elsewhere.
    /// </summary>
    private static async Task<int> SetUpAsync(HttpClient c)
    {
        using (var put = await c.PutAsJsonAsync("api/settings/sms", new { elksNumber = ElksNumber, senderNumbers = new[] { Sender } }, Ct))
            Assert.True(put.IsSuccessStatusCode, await put.Content.ReadAsStringAsync(Ct));
        var areas = (await c.GetFromJsonAsync<List<Area>>("api/areas", Json, Ct))!;
        if (areas.FirstOrDefault(a => a.Title == "SQ-linan (OCSS)") is { } existing) return existing.Id;
        var area = await PostAsync(c, "api/areas", new { title = "SQ-linan (OCSS)" });
        await PostAsync(c, "api/equipment", new { title = "T101", category = "OCSS", areaId = area });
        return area;
    }

    private async Task SmsAsync(string message)
    {
        using var client = _factory.CreateClient();
        using var response = await client.PostAsync($"api/webhooks/sms?secret={ApiFactory.WebhookSecret}", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["from"] = Sender, ["to"] = ElksNumber, ["message"] = message }), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<string> LastResultAsync(HttpClient c)
        => (await c.GetFromJsonAsync<Settings>("api/settings/sms", Json, Ct))!.LastSms!.Result;

    private static async Task<List<AlarmRow>> AlarmsOnAsync(HttpClient c, int equipmentId)
        => (await c.GetFromJsonAsync<List<AlarmRow>>($"api/alarms?equipmentId={equipmentId}&take=100", Json, Ct))!;

    private static async Task<List<Unit>> UnitsAsync(HttpClient c)
        => (await c.GetFromJsonAsync<List<Unit>>("api/equipment", Json, Ct))!;

    private static async Task<List<Unknown>> UnknownAsync(HttpClient c)
        => (await c.GetFromJsonAsync<List<Unknown>>("api/settings/sms/unrecognised", Json, Ct))!;

    [Fact]
    public async Task An_undocumented_alarm_on_a_known_unit_is_added_once()
    {
        using var c = Admin();
        var area = await SetUpAsync(c);
        var title = Unique("Q");
        var unit = await PostAsync(c, "api/equipment", new { title, areaId = area });

        await SmsAsync($"06:56 / T10-T7 Line : {title} : m777.1 Ny givare saknar signal");
        Assert.Contains("Larmet fanns inte och har lagts till", await LastResultAsync(c));
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
    public async Task An_unknown_unit_is_added_beside_the_other_OCSS_units_with_its_alarm()
    {
        using var c = Admin();
        var area = await SetUpAsync(c);
        var title = Unique("Q");

        await SmsAsync($"06:56 / T10-T7 Line : {title} : M777.3 Tändbrännare ur funktion");

        var unit = Assert.Single(await UnitsAsync(c), u => u.Title == title);
        Assert.Equal(area, unit.AreaId);
        Assert.Equal("OCSS", unit.Category);
        var alarm = Assert.Single(await AlarmsOnAsync(c, unit.Id));
        Assert.Equal("M777.3", alarm.AlarmCode);
        Assert.Equal("Tändbrännare ur funktion", alarm.Title);
        Assert.Contains($"Enheten {title} och larmet fanns inte och har lagts till", await LastResultAsync(c));
    }

    [Fact]
    public async Task An_SMS_that_is_not_an_alarm_is_kept_word_for_word_until_removed()
    {
        using var c = Admin();
        await SetUpAsync(c);
        await c.DeleteAsync("api/settings/sms/unrecognised", Ct);
        var first = $"Hej!\nOCSS omstartad {Unique("")} — kontrollera ugn 3";
        var second = $"07:02 / T10-T7 Linje T102 M206.6 {Unique("")}";

        await SmsAsync(first);
        await SmsAsync(second);

        Assert.Contains("Okända SMS", await LastResultAsync(c));
        var kept = await UnknownAsync(c);
        Assert.Equal([second, first], kept.Select(k => k.Message));
        Assert.All(kept, k => Assert.Equal(Sender, k.From));

        using (var one = await c.DeleteAsync($"api/settings/sms/unrecognised/{kept[0].Id}", Ct))
            Assert.Equal(HttpStatusCode.NoContent, one.StatusCode);
        Assert.Equal([first], (await UnknownAsync(c)).Select(k => k.Message));

        using (var all = await c.DeleteAsync("api/settings/sms/unrecognised", Ct))
            Assert.Equal(HttpStatusCode.NoContent, all.StatusCode);
        Assert.Empty(await UnknownAsync(c));
    }

    [Fact]
    public async Task Alarms_are_not_kept_as_unknown_SMS()
    {
        using var c = Admin();
        await SetUpAsync(c);
        await c.DeleteAsync("api/settings/sms/unrecognised", Ct);

        await SmsAsync($"06:56 / T10-T7 Line : T101 : M777.7 Brännarfel");

        Assert.Empty(await UnknownAsync(c));
    }

    [Fact]
    public async Task Unknown_SMS_are_admin_only()
    {
        using var reader = _factory.CreateClient();
        reader.DefaultRequestHeaders.Add("X-Api-Key", ApiFactory.ReadKey);

        using var get = await reader.GetAsync("api/settings/sms/unrecognised", Ct);
        using var delete = await reader.DeleteAsync("api/settings/sms/unrecognised", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }
}
