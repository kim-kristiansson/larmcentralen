using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Larmcentralen.Api.Tests;

/// <summary>
/// Alarm definitions through the real API: alarms on units of the same type with the same code and text share
/// one definition and its solutions; "Övrigt" units never share; text edits apply to every unit of a definition.
/// </summary>
public sealed class AlarmDefinitionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private record Created(int Id);
    private record Alarm(int Id, string Title, string? AlarmCode, string Severity, int EquipmentId, int AlarmDefinitionId,
        List<string> SameDefinitionUnits, List<Solution> Solutions);
    private record Solution(int Id, string Title, List<int> AlarmIds, List<int> AlarmDefinitionIds);
    private record Definition(int Id, string EquipmentType, int ScopeEquipmentId, string? AlarmCode, string Title);
    private record DefinitionLink(int AlarmDefinitionId, int SolutionId);
    private record AlarmLink(int AlarmId, int SolutionId);
    private record SyncAlarm(int Id, string Title, string? AlarmCode, int AlarmDefinitionId);
    private record Snapshot(List<SyncAlarm> Alarms, List<Definition> AlarmDefinitions, List<DefinitionLink> DefinitionSolutions,
        List<AlarmLink> AlarmSolutions);

    private HttpClient Admin() => factory.ClientWithKey(ApiFactory.AdminKey);

    private sealed record Plant(int Area, int T101, int T102, int A03, int Main, int N1);

    private static async Task<Plant> SeedAsync(HttpClient c)
    {
        // Definitions are shared per unit type across areas: types of their own keep tests apart.
        var id = Guid.NewGuid().ToString("N");
        var area = await PostAsync(c, "api/areas", new { title = $"Area {Guid.NewGuid():N}" });
        return new Plant(area,
            await PostAsync(c, "api/equipment", new { title = "T101", areaId = area, equipmentType = $"Härdugn {id}" }),
            await PostAsync(c, "api/equipment", new { title = "T102", areaId = area, equipmentType = $"Härdugn {id}" }),
            await PostAsync(c, "api/equipment", new { title = "A03", areaId = area, equipmentType = $"Anlöpningsugn {id}" }),
            await PostAsync(c, "api/equipment", new { title = "MAIN", areaId = area, equipmentType = "Övrigt" }),
            await PostAsync(c, "api/equipment", new { title = "N1", areaId = area }));
    }

    private static Task<int> AlarmAsync(HttpClient c, int unit, string code, string title, string severity = "Hög")
        => PostAsync(c, "api/alarms", new { title, alarmCode = code, severity, equipmentId = unit });

    private static async Task<int> PostAsync(HttpClient c, string url, object body)
    {
        using var response = await c.PostAsJsonAsync(url, body, Ct);
        Assert.True(response.IsSuccessStatusCode, $"{url}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return (await response.Content.ReadFromJsonAsync<Created>(Json, Ct))!.Id;
    }

    private static async Task<Alarm> GetAlarmAsync(HttpClient c, int id)
        => (await c.GetFromJsonAsync<Alarm>($"api/alarms/{id}", Json, Ct))!;

    [Fact]
    public async Task Same_type_code_and_text_share_a_definition_and_its_solutions()
    {
        using var c = Admin();
        var p = await SeedAsync(c);
        var t101 = await AlarmAsync(c, p.T101, "M200.1", "Temperatur elskåp");
        // Case, spacing, accents and punctuation do not matter.
        var t102 = await AlarmAsync(c, p.T102, "m200.1", "  TEMPERATUR elskap. ");

        var solution = await PostAsync(c, "api/solutions", new { title = "Kontrollera fläkten", sortOrder = 10, alarmIds = new[] { t101 } });

        var a = await GetAlarmAsync(c, t101);
        var b = await GetAlarmAsync(c, t102);
        Assert.Equal(a.AlarmDefinitionId, b.AlarmDefinitionId);
        Assert.Equal("Temperatur elskåp", b.Title);
        Assert.Equal(["T102"], a.SameDefinitionUnits);
        Assert.Equal([solution], b.Solutions.Select(s => s.Id));
        Assert.Equal([t101, t102], b.Solutions[0].AlarmIds);
    }

    [Fact]
    public async Task Other_text_other_type_and_Ovrigt_units_get_their_own_definitions()
    {
        using var c = Admin();
        var p = await SeedAsync(c);
        var t101 = await AlarmAsync(c, p.T101, "M200.0", "Nödstopp");
        var t102Other = await AlarmAsync(c, p.T102, "M200.0", "Kommunikation CP");
        var a03 = await AlarmAsync(c, p.A03, "M200.0", "Nödstopp");
        var main = await AlarmAsync(c, p.Main, "M200.0", "Nödstopp");
        var n1 = await AlarmAsync(c, p.N1, "M200.0", "Nödstopp");

        var ids = new[] { t101, t102Other, a03, main, n1 };
        var definitions = new List<int>();
        foreach (var id in ids) definitions.Add((await GetAlarmAsync(c, id)).AlarmDefinitionId);

        Assert.Equal(ids.Length, definitions.Distinct().Count());
    }

    [Fact]
    public async Task Editing_an_alarm_changes_its_definition_for_every_unit_and_a_clash_is_409()
    {
        using var c = Admin();
        var p = await SeedAsync(c);
        var t101 = await AlarmAsync(c, p.T101, "M200.1", "Temperatur elskåp");
        var t102 = await AlarmAsync(c, p.T102, "M200.1", "Temperatur elskåp");
        await AlarmAsync(c, p.T101, "M200.2", "Fläkt stoppad");

        (await c.PutAsJsonAsync($"api/alarms/{t101}",
            new { title = "Hög temperatur i elskåp", alarmCode = "M200.1", severity = "Kritisk", equipmentId = p.T101 }, Ct)).EnsureSuccessStatusCode();

        var b = await GetAlarmAsync(c, t102);
        Assert.Equal(("Hög temperatur i elskåp", "Kritisk"), (b.Title, b.Severity));

        using var clash = await c.PutAsJsonAsync($"api/alarms/{t101}",
            new { title = "Fläkt stoppad", alarmCode = "M200.2", severity = "Hög", equipmentId = p.T101 }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
    }

    [Fact]
    public async Task Own_definition_splits_one_unit_off_with_the_same_solutions()
    {
        using var c = Admin();
        var p = await SeedAsync(c);
        var t101 = await AlarmAsync(c, p.T101, "M200.1", "Temperatur elskåp");
        var t102 = await AlarmAsync(c, p.T102, "M200.1", "Temperatur elskåp");
        var solution = await PostAsync(c, "api/solutions", new { title = "S", sortOrder = 10, alarmIds = new[] { t101 } });
        var shared = (await GetAlarmAsync(c, t101)).AlarmDefinitionId;

        using var response = await c.PostAsync($"api/alarms/{t102}/own-definition", null, Ct);
        response.EnsureSuccessStatusCode();

        var b = await GetAlarmAsync(c, t102);
        Assert.NotEqual(shared, b.AlarmDefinitionId);
        Assert.Empty(b.SameDefinitionUnits);
        Assert.Equal([solution], b.Solutions.Select(s => s.Id));

        // Its text can now change alone.
        (await c.PutAsJsonAsync($"api/alarms/{t102}",
            new { title = "Elskåp varmt", alarmCode = "M200.1", severity = "Hög", equipmentId = p.T102 }, Ct)).EnsureSuccessStatusCode();
        Assert.Equal("Temperatur elskåp", (await GetAlarmAsync(c, t101)).Title);
        Assert.Equal(shared, (await GetAlarmAsync(c, t101)).AlarmDefinitionId);
    }

    [Fact]
    public async Task Deleting_the_last_alarm_of_a_definition_removes_it_and_its_own_solutions()
    {
        using var c = Admin();
        var p = await SeedAsync(c);
        var t101 = await AlarmAsync(c, p.T101, "M200.1", "Temperatur elskåp");
        var t102 = await AlarmAsync(c, p.T102, "M200.1", "Temperatur elskåp");
        var solution = await PostAsync(c, "api/solutions", new { title = "S", sortOrder = 10, alarmIds = new[] { t101 } });

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"api/alarms/{t101}", Ct)).StatusCode);
        Assert.Equal([solution], (await GetAlarmAsync(c, t102)).Solutions.Select(s => s.Id));

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"api/alarms/{t102}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"api/solutions/{solution}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Solutions_can_be_linked_by_definition_and_unlinked_per_definition()
    {
        using var c = Admin();
        var p = await SeedAsync(c);
        var t101 = await AlarmAsync(c, p.T101, "M200.1", "Temperatur elskåp");
        var a03 = await AlarmAsync(c, p.A03, "M200.1", "Elskåpstemperatur");
        var d1 = (await GetAlarmAsync(c, t101)).AlarmDefinitionId;
        var d2 = (await GetAlarmAsync(c, a03)).AlarmDefinitionId;

        var solution = await PostAsync(c, "api/solutions", new { title = "S", sortOrder = 10, alarmDefinitionIds = new[] { d1, d2 } });
        Assert.Equal([solution], (await GetAlarmAsync(c, a03)).Solutions.Select(s => s.Id));

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"api/definitions/{d2}/solutions/{solution}", Ct)).StatusCode);
        Assert.Empty((await GetAlarmAsync(c, a03)).Solutions);
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"api/definitions/{d1}/solutions/{solution}", Ct)).StatusCode);
    }

    [Fact]
    public async Task A_unit_that_changes_type_takes_its_alarms_and_their_solutions_along()
    {
        using var c = Admin();
        var p = await SeedAsync(c);
        var t101 = await AlarmAsync(c, p.T101, "M200.1", "Temperatur elskåp");
        var t102 = await AlarmAsync(c, p.T102, "M200.1", "Temperatur elskåp");
        var solution = await PostAsync(c, "api/solutions", new { title = "S", sortOrder = 10, alarmIds = new[] { t101 } });

        (await c.PutAsJsonAsync($"api/equipment/{p.T102}", new { title = "T102", areaId = p.Area, equipmentType = "Tvätt" }, Ct)).EnsureSuccessStatusCode();

        var a = await GetAlarmAsync(c, t101);
        var b = await GetAlarmAsync(c, t102);
        Assert.NotEqual(a.AlarmDefinitionId, b.AlarmDefinitionId);
        Assert.Equal([solution], b.Solutions.Select(s => s.Id));
        Assert.Equal([solution], a.Solutions.Select(s => s.Id));
    }

    [Fact]
    public async Task A_duplicate_alarm_on_the_same_unit_is_409()
    {
        using var c = Admin();
        var p = await SeedAsync(c);
        await AlarmAsync(c, p.T101, "M200.1", "Temperatur elskåp");

        using var response = await c.PostAsJsonAsync("api/alarms",
            new { title = "Temperatur elskåp", alarmCode = "M200.1", severity = "Hög", equipmentId = p.T101 }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Sync_sends_definitions_and_keeps_the_flat_shape_for_old_clients()
    {
        using var c = Admin();
        var p = await SeedAsync(c);
        var t101 = await AlarmAsync(c, p.T101, "M200.1", "Temperatur elskåp");
        var t102 = await AlarmAsync(c, p.T102, "M200.1", "Temperatur elskåp");
        var solution = await PostAsync(c, "api/solutions", new { title = "S", sortOrder = 10, alarmIds = new[] { t101 } });

        var snapshot = (await c.GetFromJsonAsync<Snapshot>("api/sync", Json, Ct))!;

        var alarm = snapshot.Alarms.Single(a => a.Id == t102);
        Assert.Equal(("Temperatur elskåp", "M200.1"), (alarm.Title, alarm.AlarmCode));
        var definition = snapshot.AlarmDefinitions.Single(d => d.Id == alarm.AlarmDefinitionId);
        Assert.StartsWith("Härdugn", definition.EquipmentType);
        Assert.Equal(0, definition.ScopeEquipmentId);
        Assert.Contains(new DefinitionLink(definition.Id, solution), snapshot.DefinitionSolutions);
        Assert.Contains(new AlarmLink(t101, solution), snapshot.AlarmSolutions);
        Assert.Contains(new AlarmLink(t102, solution), snapshot.AlarmSolutions);
    }
}
