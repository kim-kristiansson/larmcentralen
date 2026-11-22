using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Larmcentralen.Application.Interfaces;
using Larmcentralen.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Larmcentralen.Api.Tests;

/// <summary>
/// The bin (Papperskorg): removing an avdelning or a unit takes everything below it along, hides it everywhere,
/// can be undone, and is erased for good after 30 days.
/// </summary>
public sealed class TrashTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private record Created(int Id);
    private record Problem(string? Detail);
    private record TrashItem(int Id, string Kind, int ItemId, string Title, string? AreaTitle,
        int EquipmentCount, int AlarmCount, int SolutionCount, DateTime DeletedAt, DateTime EraseAt);
    private record Preview(string Kind, int ItemId, string Title, int EquipmentCount, int AlarmCount, int SolutionCount);
    private record Solution(int Id, List<int> AlarmIds);
    private record SyncRow(int Id);
    private record Snapshot(List<SyncRow> Areas, List<SyncRow> Equipment, List<SyncRow> Alarms, List<SyncRow> Solutions);

    private HttpClient Admin() => factory.ClientWithKey(ApiFactory.AdminKey);

    private static string Unique(string prefix) => $"{prefix}{Random.Shared.Next(100000, 999999)}";

    private static async Task<int> PostAsync(HttpClient c, string url, object body)
    {
        using var response = await c.PostAsJsonAsync(url, body, Ct);
        Assert.True(response.IsSuccessStatusCode, $"{url}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return (await response.Content.ReadFromJsonAsync<Created>(Json, Ct))!.Id;
    }

    private static async Task<TrashItem> RemoveAsync(HttpClient c, string url)
    {
        using var response = await c.DeleteAsync(url, Ct);
        Assert.True(response.IsSuccessStatusCode, $"{url}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return (await response.Content.ReadFromJsonAsync<TrashItem>(Json, Ct))!;
    }

    private static async Task<HttpResponseMessage> RestoreAsync(HttpClient c, int batchId)
        => await c.PostAsync($"api/trash/{batchId}/restore", null, Ct);

    private static async Task<HttpStatusCode> StatusAsync(HttpClient c, string url)
    {
        using var response = await c.GetAsync(url, Ct);
        return response.StatusCode;
    }

    private static async Task<Snapshot> SyncAsync(HttpClient c) => (await c.GetFromJsonAsync<Snapshot>("api/sync", Json, Ct))!;

    /// <summary>An avdelning with two units, two alarms each, a solution of its own on T1, and one shared by both units.</summary>
    private static async Task<(int Area, int T1, int T2, int[] T1Alarms, int[] T2Alarms, int Own, int Shared)> SeedAsync(HttpClient c)
    {
        var area = await PostAsync(c, "api/areas", new { title = Unique("Linje ") });
        var t1 = await PostAsync(c, "api/equipment", new { title = "T1", areaId = area, equipmentType = "Härdugn" });
        var t2 = await PostAsync(c, "api/equipment", new { title = "T2", areaId = area, equipmentType = "Härdugn" });
        int[] t1Alarms =
        [
            await PostAsync(c, "api/alarms", new { title = "Temperatur hög", severity = "Hög", equipmentId = t1 }),
            await PostAsync(c, "api/alarms", new { title = "Dörr öppen", severity = "Låg", equipmentId = t1 })
        ];
        int[] t2Alarms =
        [
            await PostAsync(c, "api/alarms", new { title = "Temperatur hög", severity = "Hög", equipmentId = t2 }),
            await PostAsync(c, "api/alarms", new { title = "Dörr öppen", severity = "Låg", equipmentId = t2 })
        ];
        var own = await PostAsync(c, "api/solutions", new { title = "Kontrollera dörrbrytaren", sortOrder = 10, alarmIds = new[] { t1Alarms[1] } });
        var shared = await PostAsync(c, "api/solutions", new { title = "Byt termoelement", sortOrder = 10, alarmIds = new[] { t1Alarms[0], t2Alarms[0] } });
        return (area, t1, t2, t1Alarms, t2Alarms, own, shared);
    }

    [Fact]
    public async Task Removing_a_unit_with_alarms_hides_it_with_its_alarms_and_own_solutions_and_restore_brings_it_back()
    {
        using var c = Admin();
        var s = await SeedAsync(c);

        var preview = await c.GetFromJsonAsync<Preview>($"api/trash/preview/equipment/{s.T1}", Json, Ct);
        Assert.Equal((1, 2, 1), (preview!.EquipmentCount, preview.AlarmCount, preview.SolutionCount));

        var item = await RemoveAsync(c, $"api/equipment/{s.T1}");
        Assert.Equal(("Equipment", "T1", 2, 1), (item.Kind, item.Title, item.AlarmCount, item.SolutionCount));
        Assert.Equal(item.DeletedAt.AddDays(30), item.EraseAt);

        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(c, $"api/equipment/{s.T1}"));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(c, $"api/alarms/{s.T1Alarms[0]}"));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(c, $"api/solutions/{s.Own}"));
        var sync = await SyncAsync(c);
        Assert.DoesNotContain(sync.Equipment, e => e.Id == s.T1);
        Assert.DoesNotContain(sync.Alarms, a => s.T1Alarms.Contains(a.Id));
        Assert.DoesNotContain(sync.Solutions, x => x.Id == s.Own);

        // The shared solution stays for T2, without the removed alarm.
        var shared = await c.GetFromJsonAsync<Solution>($"api/solutions/{s.Shared}", Json, Ct);
        Assert.Equal([s.T2Alarms[0]], shared!.AlarmIds);

        var list = await c.GetFromJsonAsync<List<TrashItem>>("api/trash", Json, Ct);
        Assert.Contains(list!, x => x.Id == item.Id);

        using (var restore = await RestoreAsync(c, item.Id))
            Assert.Equal(HttpStatusCode.OK, restore.StatusCode);

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(c, $"api/equipment/{s.T1}"));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(c, $"api/solutions/{s.Own}"));
        shared = await c.GetFromJsonAsync<Solution>($"api/solutions/{s.Shared}", Json, Ct);
        Assert.Equal([s.T1Alarms[0], s.T2Alarms[0]], shared!.AlarmIds.Order().ToList());
        Assert.DoesNotContain((await c.GetFromJsonAsync<List<TrashItem>>("api/trash", Json, Ct))!, x => x.Id == item.Id);
    }

    [Fact]
    public async Task Removing_an_avdelning_takes_all_its_units_alarms_and_solutions_and_restore_brings_them_back()
    {
        using var c = Admin();
        var s = await SeedAsync(c);

        var item = await RemoveAsync(c, $"api/areas/{s.Area}");
        Assert.Equal(("Area", 2, 4, 2), (item.Kind, item.EquipmentCount, item.AlarmCount, item.SolutionCount));

        var sync = await SyncAsync(c);
        Assert.DoesNotContain(sync.Areas, a => a.Id == s.Area);
        Assert.DoesNotContain(sync.Equipment, e => e.Id == s.T1 || e.Id == s.T2);
        Assert.DoesNotContain(sync.Solutions, x => x.Id == s.Shared || x.Id == s.Own);

        using (var restore = await RestoreAsync(c, item.Id))
            Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        sync = await SyncAsync(c);
        Assert.Contains(sync.Areas, a => a.Id == s.Area);
        Assert.Equal(4, sync.Alarms.Count(a => s.T1Alarms.Contains(a.Id) || s.T2Alarms.Contains(a.Id)));
        Assert.Contains(sync.Solutions, x => x.Id == s.Shared);
    }

    [Fact]
    public async Task A_unit_cannot_be_restored_while_its_avdelning_is_in_the_bin()
    {
        using var c = Admin();
        var s = await SeedAsync(c);
        var unit = await RemoveAsync(c, $"api/equipment/{s.T1}");
        var area = await RemoveAsync(c, $"api/areas/{s.Area}");

        using (var refused = await RestoreAsync(c, unit.Id))
        {
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Contains("Återställ den först", (await refused.Content.ReadFromJsonAsync<Problem>(Json, Ct))!.Detail);
        }

        using (var restoreArea = await RestoreAsync(c, area.Id)) Assert.Equal(HttpStatusCode.OK, restoreArea.StatusCode);
        // The avdelning comes back without the unit that was removed on its own.
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(c, $"api/equipment/{s.T1}"));
        using (var restoreUnit = await RestoreAsync(c, unit.Id)) Assert.Equal(HttpStatusCode.OK, restoreUnit.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(c, $"api/equipment/{s.T1}"));
    }

    [Fact]
    public async Task Restoring_a_unit_whose_name_was_taken_meanwhile_is_409()
    {
        using var c = Admin();
        var s = await SeedAsync(c);
        var item = await RemoveAsync(c, $"api/equipment/{s.T1}");
        await PostAsync(c, "api/equipment", new { title = "t1", areaId = s.Area });

        using var refused = await RestoreAsync(c, item.Id);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    }

    [Fact]
    public async Task A_new_avdelning_cannot_take_the_name_of_one_in_the_bin()
    {
        using var c = Admin();
        var title = Unique("Linje ");
        var area = await PostAsync(c, "api/areas", new { title });
        await RemoveAsync(c, $"api/areas/{area}");

        using var response = await c.PostAsJsonAsync("api/areas", new { title }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("papperskorgen", (await response.Content.ReadFromJsonAsync<Problem>(Json, Ct))!.Detail);
    }

    [Fact]
    public async Task Deleting_an_alarm_keeps_a_solution_it_shares_with_an_alarm_in_the_bin()
    {
        using var c = Admin();
        var s = await SeedAsync(c);
        var item = await RemoveAsync(c, $"api/equipment/{s.T1}");

        // The shared solution now only shows on T2's alarm; deleting that alarm must not erase it,
        // since T1's alarm (in the bin) still uses it.
        using (var delete = await c.DeleteAsync($"api/alarms/{s.T2Alarms[0]}", Ct)) Assert.True(delete.IsSuccessStatusCode);
        using (var restore = await RestoreAsync(c, item.Id)) Assert.Equal(HttpStatusCode.OK, restore.StatusCode);

        var shared = await c.GetFromJsonAsync<Solution>($"api/solutions/{s.Shared}", Json, Ct);
        Assert.Equal([s.T1Alarms[0]], shared!.AlarmIds);
    }

    [Fact]
    public async Task Erase_now_removes_it_for_good()
    {
        using var c = Admin();
        var s = await SeedAsync(c);
        var item = await RemoveAsync(c, $"api/equipment/{s.T1}");

        using (var erase = await c.DeleteAsync($"api/trash/{item.Id}", Ct)) Assert.Equal(HttpStatusCode.NoContent, erase.StatusCode);

        using (var restore = await RestoreAsync(c, item.Id)) Assert.Equal(HttpStatusCode.NotFound, restore.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Equipment.IgnoreQueryFilters().AnyAsync(e => e.Id == s.T1, Ct));
        Assert.False(await db.Alarms.IgnoreQueryFilters().AnyAsync(a => s.T1Alarms.Contains(a.Id), Ct));
        Assert.False(await db.Solutions.IgnoreQueryFilters().AnyAsync(x => x.Id == s.Own, Ct));
        Assert.True(await db.Solutions.AnyAsync(x => x.Id == s.Shared, Ct));
    }

    [Fact]
    public async Task Removals_older_than_30_days_are_erased_and_newer_ones_kept()
    {
        using var c = Admin();
        var old = await SeedAsync(c);
        var recent = await SeedAsync(c);
        var now = DateTime.UtcNow;

        await using var scope = factory.Services.CreateAsyncScope();
        var trash = scope.ServiceProvider.GetRequiredService<ITrashService>();
        // A unit removed on its own first, then its avdelning: both old.
        var oldUnit = await trash.MoveToTrashAsync(null, old.T1, now.AddDays(-40));
        var oldArea = await trash.MoveToTrashAsync(old.Area, null, now.AddDays(-31));
        var newer = await trash.MoveToTrashAsync(recent.Area, null, now.AddDays(-29));

        await trash.EraseExpiredAsync(now);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ChangeTracker.Clear();
        Assert.False(await db.Areas.IgnoreQueryFilters().AnyAsync(a => a.Id == old.Area, Ct));
        Assert.False(await db.Equipment.IgnoreQueryFilters().AnyAsync(e => e.Id == old.T1 || e.Id == old.T2, Ct));
        Assert.False(await db.TrashBatches.AnyAsync(b => b.Id == oldUnit.Value!.Id || b.Id == oldArea.Value!.Id, Ct));
        Assert.True(await db.Areas.IgnoreQueryFilters().AnyAsync(a => a.Id == recent.Area, Ct));
        Assert.True(await db.TrashBatches.AnyAsync(b => b.Id == newer.Value!.Id, Ct));
    }

    [Theory]
    [InlineData("api/trash")]
    [InlineData("api/trash/preview/areas/1")]
    public async Task The_bin_is_for_admins_only(string url)
    {
        using var c = factory.ClientWithKey(ApiFactory.ReadKey);

        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(c, url));
    }
}
