using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Larmcentralen.Api.Tests;

/// <summary>Reusable alarm codes: each code once in the list, each alarm with its own text, one alarm per code on a unit.</summary>
public sealed class AlarmCodeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private record Created(int Id);
    private record Alarm(int Id, string Title, string? AlarmCode, int? AlarmCodeId, int EquipmentId);
    private record CodeRow(int Id, string Code, string? Description, int AlarmCount);
    private record SyncAlarm(int Id, string Title, string? AlarmCode, int? AlarmCodeId);
    private record SyncCode(int Id, string Code);
    private record Snapshot(List<SyncAlarm> Alarms, List<SyncCode> AlarmCodes);
    private record ListItem(int Id, string? AlarmCode, string EquipmentTitle);

    private HttpClient Admin() => factory.ClientWithKey(ApiFactory.AdminKey);

    private static async Task<int> PostAsync(HttpClient c, string url, object body)
    {
        using var response = await c.PostAsJsonAsync(url, body, Ct);
        Assert.True(response.IsSuccessStatusCode, $"{url}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return (await response.Content.ReadFromJsonAsync<Created>(Json, Ct))!.Id;
    }

    private static async Task<(int Area, int T101, int T102)> SeedAsync(HttpClient c)
    {
        var area = await PostAsync(c, "api/areas", new { title = $"Area {Guid.NewGuid():N}" });
        return (area,
            await PostAsync(c, "api/equipment", new { title = "T101", areaId = area, equipmentType = "Härdugn" }),
            await PostAsync(c, "api/equipment", new { title = "T102", areaId = area, equipmentType = "Härdugn" }));
    }

    /// <summary>A code no other test uses, since the code list is shared by the whole database.</summary>
    private static string NewCode() => $"X{Random.Shared.Next(100000, 999999)}.{Random.Shared.Next(0, 8)}";

    private static async Task<Alarm> GetAsync(HttpClient c, int id) => (await c.GetFromJsonAsync<Alarm>($"api/alarms/{id}", Json, Ct))!;

    private static async Task<List<CodeRow>> CodesAsync(HttpClient c)
        => (await c.GetFromJsonAsync<List<CodeRow>>("api/alarm-codes", Json, Ct))!;

    [Fact]
    public async Task The_same_code_on_two_units_is_one_code_row_and_each_alarm_keeps_its_own_text()
    {
        using var c = Admin();
        var (_, t101, t102) = await SeedAsync(c);
        var code = NewCode();

        var a1 = await PostAsync(c, "api/alarms", new { title = "Temperatur elskåp", alarmCode = code, severity = "Hög", equipmentId = t101 });
        var a2 = await PostAsync(c, "api/alarms", new { title = "ELSKÅPSTEMPERATUR", alarmCode = $" {code.ToLowerInvariant()} ", severity = "Låg", equipmentId = t102 });

        var x = await GetAsync(c, a1);
        var y = await GetAsync(c, a2);
        Assert.Equal(x.AlarmCodeId, y.AlarmCodeId);
        Assert.Equal((code, code), (x.AlarmCode, y.AlarmCode));
        Assert.Equal(("Temperatur elskåp", "ELSKÅPSTEMPERATUR"), (x.Title, y.Title));

        var row = Assert.Single(await CodesAsync(c), r => r.Code == code);
        Assert.Equal(2, row.AlarmCount);
    }

    [Fact]
    public async Task Picking_a_code_by_id_works_and_a_unit_cannot_have_the_same_code_twice()
    {
        using var c = Admin();
        var (_, t101, t102) = await SeedAsync(c);
        var code = await PostAsync(c, "api/alarm-codes", new { code = NewCode() });

        var a1 = await PostAsync(c, "api/alarms", new { title = "Ett", severity = "Hög", equipmentId = t101, alarmCodeId = code });
        Assert.Equal(code, (await GetAsync(c, a1)).AlarmCodeId);

        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("api/alarms",
            new { title = "Två", severity = "Hög", equipmentId = t101, alarmCodeId = code }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("api/alarms",
            new { title = "Tre", severity = "Hög", equipmentId = t101, alarmCodeId = 987654 }, Ct)).StatusCode);

        // Rows without a code are not limited.
        await PostAsync(c, "api/alarms", new { title = "Utan kod 1", severity = "Låg", equipmentId = t101 });
        await PostAsync(c, "api/alarms", new { title = "Utan kod 2", severity = "Låg", equipmentId = t101 });

        // Moving a1's code to T102 by editing: allowed there, and the code can be changed or removed.
        (await c.PutAsJsonAsync($"api/alarms/{a1}", new { title = "Ett", severity = "Hög", equipmentId = t102, alarmCodeId = code }, Ct)).EnsureSuccessStatusCode();
        Assert.Equal(t102, (await GetAsync(c, a1)).EquipmentId);
        (await c.PutAsJsonAsync($"api/alarms/{a1}", new { title = "Ett", severity = "Hög", equipmentId = t102 }, Ct)).EnsureSuccessStatusCode();
        Assert.Null((await GetAsync(c, a1)).AlarmCodeId);
    }

    [Fact]
    public async Task Codes_are_added_upper_case_once_and_stay_when_their_alarms_go()
    {
        using var c = Admin();
        var (_, t101, _) = await SeedAsync(c);
        var code = NewCode();

        var id = await PostAsync(c, "api/alarm-codes", new { code = $"  {code.ToLowerInvariant()} ", description = "Ny givare" });
        var row = Assert.Single(await CodesAsync(c), r => r.Id == id);
        Assert.Equal((code, "Ny givare", 0), (row.Code, row.Description, row.AlarmCount));

        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("api/alarm-codes", new { code }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("api/alarm-codes", new { code = "  " }, Ct)).StatusCode);

        var alarm = await PostAsync(c, "api/alarms", new { title = "Ett", severity = "Hög", equipmentId = t101, alarmCodeId = id });
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"api/alarms/{alarm}", Ct)).StatusCode);
        Assert.Single(await CodesAsync(c), r => r.Id == id);
    }

    [Fact]
    public async Task Searching_a_code_lists_every_units_alarm_with_it()
    {
        using var c = Admin();
        var (_, t101, t102) = await SeedAsync(c);
        var code = NewCode();
        await PostAsync(c, "api/alarms", new { title = "Ett", alarmCode = code, severity = "Hög", equipmentId = t101 });
        await PostAsync(c, "api/alarms", new { title = "Annan text", alarmCode = code, severity = "Hög", equipmentId = t102 });

        var hits = (await c.GetFromJsonAsync<List<ListItem>>($"api/alarms?search={Uri.EscapeDataString(code)}&take=50", Json, Ct))!;

        Assert.Equal(["T101", "T102"], hits.Where(h => h.AlarmCode == code).Select(h => h.EquipmentTitle).Order());
    }

    [Fact]
    public async Task Sync_lists_each_code_once_and_alarms_carry_both_the_id_and_the_text()
    {
        using var c = Admin();
        var (_, t101, t102) = await SeedAsync(c);
        var code = NewCode();
        var a1 = await PostAsync(c, "api/alarms", new { title = "Ett", alarmCode = code, severity = "Hög", equipmentId = t101 });
        await PostAsync(c, "api/alarms", new { title = "Två", alarmCode = code, severity = "Hög", equipmentId = t102 });

        var snapshot = (await c.GetFromJsonAsync<Snapshot>("api/sync", Json, Ct))!;

        var row = Assert.Single(snapshot.AlarmCodes, x => x.Code == code);
        Assert.Equal(snapshot.AlarmCodes.Count, snapshot.AlarmCodes.Select(x => x.Code).Distinct().Count());
        var alarm = snapshot.Alarms.Single(a => a.Id == a1);
        Assert.Equal((row.Id, code, "Ett"), (alarm.AlarmCodeId!.Value, alarm.AlarmCode, alarm.Title));
    }
}
