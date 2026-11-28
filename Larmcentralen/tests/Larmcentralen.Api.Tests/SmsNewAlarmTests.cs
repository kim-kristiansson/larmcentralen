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
/// SMS text as the unit's own text, priority Låg. The line in the SMS ("T10-T7 Line") decides the avdelning
/// (SQ…); nothing new is added for an unknown line.
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

    /// <summary>
    /// The numbers, and this class's one SQ avdelning (the line T10-T7 belongs to). Its database is its own, so no
    /// other test adds an avdelning starting with SQ here.
    /// </summary>
    private static async Task<int> SetUpAsync(HttpClient c)
    {
        using (var put = await c.PutAsJsonAsync("api/settings/sms", new { elksNumber = ElksNumber, senderNumbers = new[] { Sender } }, Ct))
            Assert.True(put.IsSuccessStatusCode, await put.Content.ReadAsStringAsync(Ct));
        var areas = (await c.GetFromJsonAsync<List<Area>>("api/areas", Json, Ct))!;
        return areas.FirstOrDefault(a => a.Title == "SQ-linan (OCSS)")?.Id
               ?? await PostAsync(c, "api/areas", new { title = "SQ-linan (OCSS)" });
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

    private static async Task<List<Unit>> UnitsAsync(HttpClient c)
        => (await c.GetFromJsonAsync<List<Unit>>("api/equipment", Json, Ct))!;

    [Theory]
    [InlineData("06:56 / T10-T7 Line : T102 : M206.6 Ingen svarssignal ytterlucka", "T10-T7", "T102", "M206.6", "Ingen svarssignal ytterlucka")]
    [InlineData("T10-T7 Line : T102 : M206.6", "T10-T7", "T102", "M206.6", "")]
    [InlineData("06:56 / Line : T102 : M206.6 Text", null, "T102", "M206.6", "Text")]
    [InlineData("07:01 / T3-T1 Line: W1 : M200.0 Batteri", "T3-T1", "W1", "M200.0", "Batteri")]
    public void The_line_unit_code_and_text_are_read(string sms, string? line, string unit, string code, string text)
        => Assert.Equal((line, unit, code, text), SmsAlarmService.ParseSms(sms));

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
    public async Task An_unknown_unit_on_the_SQ_line_is_added_there_with_its_alarm()
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

    [Theory]
    [InlineData("06:56 / X9-X1 Line", "Linjen \"X9-X1\" är okänd")]
    [InlineData("06:56 / Line", "SMS:et anger ingen linje")]
    public async Task Nothing_new_is_added_for_an_unknown_line(string start, string reason)
    {
        using var c = Admin();
        var area = await SetUpAsync(c);
        var known = Unique("Q");
        var knownId = await PostAsync(c, "api/equipment", new { title = known, areaId = area });
        var unknown = Unique("Q");

        await SmsAsync($"{start} : {unknown} : M777.4 Okänd enhet");
        Assert.Contains(reason, await LastResultAsync(c));
        Assert.DoesNotContain(await UnitsAsync(c), u => u.Title == unknown);

        await SmsAsync($"{start} : {known} : M777.5 Okänt larm");
        Assert.Contains(reason, await LastResultAsync(c));
        Assert.Empty(await AlarmsOnAsync(c, knownId));
    }

    [Fact]
    public async Task A_documented_alarm_is_found_whatever_the_line()
    {
        using var c = Admin();
        var area = await SetUpAsync(c);
        var title = Unique("Q");
        var unit = await PostAsync(c, "api/equipment", new { title, areaId = area });
        await PostAsync(c, "api/alarms", new { title = "Brännarfel", alarmCode = "M777.6", severity = "Kritisk", equipmentId = unit });

        await SmsAsync($"06:56 / X9-X1 Line : {title} : M777.6 Brännarfel");

        Assert.Contains("Avisering skickad", await LastResultAsync(c));
        Assert.Single(_alerts.Sent);
    }
}
