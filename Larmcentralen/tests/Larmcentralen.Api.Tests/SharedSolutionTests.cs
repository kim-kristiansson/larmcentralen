using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Larmcentralen.Api.Tests;

/// <summary>Solutions shared by several alarms, through the real API and its SQLite database.</summary>
public sealed class SharedSolutionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private record Created(int Id);
    private record Solution(int Id, string Title, string? Content, int AlarmId, List<int> AlarmIds);
    private record Link(int AlarmId, int SolutionId);
    private record SyncEquipment(int Id, string Title, string? EquipmentType);
    private record Snapshot(List<SyncEquipment> Equipment, List<Solution> Solutions, List<Link> AlarmSolutions);

    private HttpClient Admin() => factory.ClientWithKey(ApiFactory.AdminKey);

    /// <summary>
    /// A new area with two furnaces of the same type and one alarm each: same code, different text, so they are
    /// two definitions (alarms with the same text would share one, see AlarmDefinitionTests).
    /// </summary>
    private static async Task<(int T101Alarm, int T102Alarm, int T102Equipment)> SeedAsync(HttpClient c)
    {
        // Definitions are shared per unit type across areas: a type of its own keeps tests apart.
        var type = $"Härdugn {Guid.NewGuid():N}";
        var area = await PostAsync(c, "api/areas", new { title = $"Area {Guid.NewGuid():N}" });
        var t101 = await PostAsync(c, "api/equipment", new { title = "T101", areaId = area, equipmentType = type });
        var t102 = await PostAsync(c, "api/equipment", new { title = "T102", areaId = area, equipmentType = type });
        var a1 = await PostAsync(c, "api/alarms", new { title = "Temperatur för hög", alarmCode = "M200.1", severity = "Hög", equipmentId = t101 });
        var a2 = await PostAsync(c, "api/alarms", new { title = "Temperatur elskåp", alarmCode = "M200.1", severity = "Hög", equipmentId = t102 });
        return (a1, a2, t102);
    }

    private static async Task<int> PostAsync(HttpClient c, string url, object body)
    {
        using var response = await c.PostAsJsonAsync(url, body, Ct);
        Assert.True(response.IsSuccessStatusCode, $"{url}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return (await response.Content.ReadFromJsonAsync<Created>(Json, Ct))!.Id;
    }

    private static StringContent JsonBody(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<Solution> GetSolutionAsync(HttpClient c, int id)
        => (await c.GetFromJsonAsync<Solution>($"api/solutions/{id}", Json, Ct))!;

    private static async Task<List<Solution>> ByAlarmAsync(HttpClient c, int alarmId)
        => (await c.GetFromJsonAsync<List<Solution>>($"api/solutions/by-alarm/{alarmId}", Json, Ct))!;

    [Fact]
    public async Task Create_with_legacy_AlarmId_links_that_alarm()
    {
        using var c = Admin();
        var (a1, _, _) = await SeedAsync(c);

        var id = await PostAsync(c, "api/solutions", new { title = "Byt termoelement", sortOrder = 10, alarmId = a1 });

        var s = await GetSolutionAsync(c, id);
        Assert.Equal(a1, s.AlarmId);
        Assert.Equal([a1], s.AlarmIds);
    }

    [Fact]
    public async Task Create_with_AlarmIds_shares_the_solution_and_both_alarms_show_it()
    {
        using var c = Admin();
        var (a1, a2, _) = await SeedAsync(c);

        var id = await PostAsync(c, "api/solutions", new { title = "Delad", sortOrder = 10, alarmIds = new[] { a2, a1 } });

        var s = await GetSolutionAsync(c, id);
        Assert.Equal([a1, a2], s.AlarmIds);
        Assert.Equal(Math.Min(a1, a2), s.AlarmId);
        Assert.Contains(await ByAlarmAsync(c, a1), x => x.Id == id);
        Assert.Contains(await ByAlarmAsync(c, a2), x => x.Id == id);
    }

    [Theory]
    [InlineData("""{"title":"x","sortOrder":10}""")]
    [InlineData("""{"title":"x","sortOrder":10,"alarmIds":null}""")]
    [InlineData("""{"title":"x","sortOrder":10,"alarmIds":[]}""")]
    [InlineData("""{"title":"x","sortOrder":10,"alarmIds":[987654]}""")]
    [InlineData("""{"title":"x","sortOrder":10,"alarmId":987654}""")]
    public async Task Create_without_valid_alarms_is_400(string body)
    {
        using var c = Admin();
        using var response = await c.PostAsync("api/solutions", JsonBody(body), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_with_null_AlarmIds_keeps_links_and_a_list_replaces_them()
    {
        using var c = Admin();
        var (a1, a2, _) = await SeedAsync(c);
        var id = await PostAsync(c, "api/solutions", new { title = "S", sortOrder = 10, alarmIds = new[] { a1 } });

        // Older client: no AlarmIds.
        (await c.PutAsJsonAsync($"api/solutions/{id}", new { title = "S2", sortOrder = 10 }, Ct)).EnsureSuccessStatusCode();
        var s = await GetSolutionAsync(c, id);
        Assert.Equal("S2", s.Title);
        Assert.Equal([a1], s.AlarmIds);

        (await c.PutAsJsonAsync($"api/solutions/{id}", new { title = "S2", sortOrder = 10, alarmIds = new[] { a2 } }, Ct)).EnsureSuccessStatusCode();
        Assert.Equal([a2], (await GetSolutionAsync(c, id)).AlarmIds);

        (await c.PutAsJsonAsync($"api/solutions/{id}", new { title = "S2", sortOrder = 10, alarmIds = new[] { a1, a2 } }, Ct)).EnsureSuccessStatusCode();
        Assert.Equal([a1, a2], (await GetSolutionAsync(c, id)).AlarmIds);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[987654]")]
    public async Task Update_with_empty_or_unknown_AlarmIds_is_400_and_changes_nothing(string ids)
    {
        using var c = Admin();
        var (a1, _, _) = await SeedAsync(c);
        var id = await PostAsync(c, "api/solutions", new { title = "S", sortOrder = 10, alarmIds = new[] { a1 } });

        using var response = await c.PutAsync($"api/solutions/{id}",
            JsonBody("{\"title\":\"Ändrad\",\"sortOrder\":10,\"alarmIds\":" + ids + "}"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var s = await GetSolutionAsync(c, id);
        Assert.Equal("S", s.Title);
        Assert.Equal([a1], s.AlarmIds);
    }

    [Fact]
    public async Task Unlink_removes_one_alarm_and_the_last_link_is_409()
    {
        using var c = Admin();
        var (a1, a2, _) = await SeedAsync(c);
        var id = await PostAsync(c, "api/solutions", new { title = "S", sortOrder = 10, alarmIds = new[] { a1, a2 } });

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"api/alarms/{a2}/solutions/{id}", Ct)).StatusCode);
        Assert.Equal([a1], (await GetSolutionAsync(c, id)).AlarmIds);
        Assert.Empty(await ByAlarmAsync(c, a2));

        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"api/alarms/{a1}/solutions/{id}", Ct)).StatusCode);
        Assert.Equal([a1], (await GetSolutionAsync(c, id)).AlarmIds);

        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"api/alarms/{a2}/solutions/{id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Delete_solution_removes_it_from_every_alarm()
    {
        using var c = Admin();
        var (a1, a2, _) = await SeedAsync(c);
        var id = await PostAsync(c, "api/solutions", new { title = "S", sortOrder = 10, alarmIds = new[] { a1, a2 } });

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"api/solutions/{id}", Ct)).StatusCode);

        Assert.Empty(await ByAlarmAsync(c, a1));
        Assert.Empty(await ByAlarmAsync(c, a2));
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"api/solutions/{id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Detach_gives_one_alarm_its_own_copy_and_leaves_the_other_unit_unchanged()
    {
        using var c = Admin();
        var (a1, a2, _) = await SeedAsync(c);
        var id = await PostAsync(c, "api/solutions", new { title = "S", content = "<p>Steg</p>", sortOrder = 20, alarmIds = new[] { a1, a2 } });

        using var response = await c.PostAsync($"api/alarms/{a2}/solutions/{id}/detach", null, Ct);
        response.EnsureSuccessStatusCode();
        var copy = (await response.Content.ReadFromJsonAsync<Solution>(Json, Ct))!;

        Assert.NotEqual(id, copy.Id);
        Assert.Equal([a2], copy.AlarmIds);
        Assert.Equal(("S", "<p>Steg</p>"), (copy.Title, copy.Content));
        var original = await GetSolutionAsync(c, id);
        Assert.Equal([a1], original.AlarmIds);
        Assert.Equal(("S", "<p>Steg</p>"), (original.Title, original.Content));
        Assert.Equal([id], (await ByAlarmAsync(c, a1)).Select(s => s.Id));
        Assert.Equal([copy.Id], (await ByAlarmAsync(c, a2)).Select(s => s.Id));

        // Editing the copy does not touch the original.
        (await c.PutAsJsonAsync($"api/solutions/{copy.Id}", new { title = "Egen", sortOrder = 20 }, Ct)).EnsureSuccessStatusCode();
        Assert.Equal("S", (await GetSolutionAsync(c, id)).Title);
    }

    [Fact]
    public async Task Deleting_an_alarm_removes_its_own_solutions_but_keeps_shared_ones()
    {
        using var c = Admin();
        var (a1, a2, _) = await SeedAsync(c);
        var own = await PostAsync(c, "api/solutions", new { title = "Egen", sortOrder = 10, alarmIds = new[] { a1 } });
        var shared = await PostAsync(c, "api/solutions", new { title = "Delad", sortOrder = 20, alarmIds = new[] { a1, a2 } });

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"api/alarms/{a1}", Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"api/solutions/{own}", Ct)).StatusCode);
        Assert.Equal([a2], (await GetSolutionAsync(c, shared)).AlarmIds);
    }

    [Fact]
    public async Task Sync_lists_links_and_equipment_types_and_keeps_AlarmId_for_old_clients()
    {
        using var c = Admin();
        var (a1, a2, t102) = await SeedAsync(c);
        var id = await PostAsync(c, "api/solutions", new { title = "Delad", sortOrder = 10, alarmIds = new[] { a2, a1 } });

        var json = await c.GetStringAsync("api/sync", Ct);
        var snapshot = JsonSerializer.Deserialize<Snapshot>(json, Json)!;

        Assert.Contains(new Link(a1, id), snapshot.AlarmSolutions);
        Assert.Contains(new Link(a2, id), snapshot.AlarmSolutions);
        Assert.StartsWith("Härdugn", snapshot.Equipment.Single(e => e.Id == t102).EquipmentType);

        // The client record shape from before shared solutions: AlarmId must still be filled in.
        var old = JsonSerializer.Deserialize<OldSnapshot>(json, Json)!;
        Assert.Equal(Math.Min(a1, a2), old.Solutions.Single(s => s.Id == id).AlarmId);
        Assert.All(old.Solutions, s => Assert.NotEqual(0, s.AlarmId));
    }

    [Fact]
    public async Task Equipment_type_is_kept_when_an_old_client_updates_without_it()
    {
        using var c = Admin();
        var (_, _, t102) = await SeedAsync(c);
        var areaId = (await c.GetFromJsonAsync<JsonElement>($"api/equipment/{t102}", Ct)).GetProperty("areaId").GetInt32();

        (await c.PutAsJsonAsync($"api/equipment/{t102}", new { title = "T102", areaId }, Ct)).EnsureSuccessStatusCode();
        Assert.StartsWith("Härdugn", (await c.GetFromJsonAsync<JsonElement>($"api/equipment/{t102}", Ct)).GetProperty("equipmentType").GetString());

        (await c.PutAsJsonAsync($"api/equipment/{t102}", new { title = "T102", areaId, equipmentType = "" }, Ct)).EnsureSuccessStatusCode();
        Assert.Equal(JsonValueKind.Null, (await c.GetFromJsonAsync<JsonElement>($"api/equipment/{t102}", Ct)).GetProperty("equipmentType").ValueKind);
    }

    // Copies of the client's records as they were before shared solutions.
    private record OldSolution(int Id, string Title, string? Content, int SortOrder, string? EstimatedTime, int AlarmId, DateTime CreatedAt, DateTime? UpdatedAt);
    private record OldEquipment(int Id, string Title, string? DisplayName, int AreaId, DateTime? UpdatedAt);
    private record OldSnapshot(DateTime GeneratedAt, List<OldEquipment> Equipment, List<OldSolution> Solutions);
}
