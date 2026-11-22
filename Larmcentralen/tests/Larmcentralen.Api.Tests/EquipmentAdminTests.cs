using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Larmcentralen.Api.Tests;

/// <summary>Admins add, change and remove areas and units. Removing goes through the bin: see TrashTests.</summary>
public sealed class EquipmentAdminTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private record Created(int Id);
    private record Problem(string? Detail);
    private record Unit(int Id, string Title, string? DisplayName, string? Description, int AreaId, string? EquipmentType);
    private record AreaRow(int Id, string Title, string? Description, int EquipmentCount);

    private HttpClient Admin() => factory.ClientWithKey(ApiFactory.AdminKey);

    private static string Unique(string prefix) => $"{prefix}{Random.Shared.Next(100000, 999999)}";

    private static async Task<int> PostAsync(HttpClient c, string url, object body)
    {
        using var response = await c.PostAsJsonAsync(url, body, Ct);
        Assert.True(response.IsSuccessStatusCode, $"{url}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return (await response.Content.ReadFromJsonAsync<Created>(Json, Ct))!.Id;
    }

    private static async Task<string?> DetailAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<Problem>(Json, Ct))?.Detail;

    [Fact]
    public async Task Admin_can_add_change_and_remove_a_unit()
    {
        using var c = Admin();
        var area = await PostAsync(c, "api/areas", new { title = Unique("Avdelning ") });
        var other = await PostAsync(c, "api/areas", new { title = Unique("Avdelning ") });
        var title = Unique("T");

        var id = await PostAsync(c, "api/equipment", new { title = $"  {title} ", areaId = area, equipmentType = "Härdugn" });
        var unit = await c.GetFromJsonAsync<Unit>($"api/equipment/{id}", Json, Ct);
        Assert.Equal((title, "Härdugn", area), (unit!.Title, unit.EquipmentType, unit.AreaId));

        using (var put = await c.PutAsJsonAsync($"api/equipment/{id}",
                   new { title, displayName = "Härdugn 9", areaId = other, equipmentType = "Tvätt" }, Ct))
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        unit = await c.GetFromJsonAsync<Unit>($"api/equipment/{id}", Json, Ct);
        Assert.Equal(("Härdugn 9", "Tvätt", other), (unit!.DisplayName, unit.EquipmentType, unit.AreaId));

        // Removing moves it to the bin (see TrashTests).
        using (var delete = await c.DeleteAsync($"api/equipment/{id}", Ct))
            Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        using (var gone = await c.GetAsync($"api/equipment/{id}", Ct))
            Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Update_without_optional_fields_keeps_them_and_an_empty_string_clears()
    {
        using var c = Admin();
        var area = await PostAsync(c, "api/areas", new { title = Unique("Avdelning "), description = "Larm från OCSS." });
        var title = Unique("W");
        var id = await PostAsync(c, "api/equipment", new { title, displayName = "Tvätt 1", description = "Står vid porten", areaId = area });

        // The app only knows title, name, type and area: what it doesn't send must survive.
        using (var put = await c.PutAsJsonAsync($"api/equipment/{id}", new { title, displayName = "", areaId = area, equipmentType = "Tvätt" }, Ct))
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        var unit = await c.GetFromJsonAsync<Unit>($"api/equipment/{id}", Json, Ct);
        Assert.Equal((null, "Står vid porten"), (unit!.DisplayName, unit.Description));

        var newName = Unique("Nytt namn ");
        using (var put = await c.PutAsJsonAsync($"api/areas/{area}", new { title = newName }, Ct))
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        var row = await c.GetFromJsonAsync<AreaRow>($"api/areas/{area}", Json, Ct);
        Assert.Equal((newName, "Larm från OCSS."), (row!.Title, row.Description));
    }

    [Fact]
    public async Task A_unit_title_taken_in_the_same_area_is_409_in_any_case_but_another_area_may_use_it()
    {
        using var c = Admin();
        var a1 = await PostAsync(c, "api/areas", new { title = Unique("Avdelning ") });
        var a2 = await PostAsync(c, "api/areas", new { title = Unique("Avdelning ") });
        var title = Unique("A");
        var first = await PostAsync(c, "api/equipment", new { title, areaId = a1 });

        using (var response = await c.PostAsJsonAsync("api/equipment", new { title = title.ToLowerInvariant(), areaId = a1 }, Ct))
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains(title, await DetailAsync(response));
        }

        var second = await PostAsync(c, "api/equipment", new { title, areaId = a2 });

        // Moving the second into the first area would clash too.
        using var move = await c.PutAsJsonAsync($"api/equipment/{second}", new { title, areaId = a1 }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, move.StatusCode);
        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("   ", 1)]
    [InlineData("T1", 999999)]
    public async Task A_unit_without_title_or_with_an_unknown_area_is_400(string title, int areaId)
    {
        using var c = Admin();

        using var response = await c.PostAsJsonAsync("api/equipment", new { title, areaId }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_area_name_that_is_taken_is_409()
    {
        using var c = Admin();
        var title = Unique("SQ-linjen ");
        await PostAsync(c, "api/areas", new { title });

        using var response = await c.PostAsJsonAsync("api/areas", new { title = title.ToUpperInvariant() }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task The_area_list_counts_its_units()
    {
        using var c = Admin();
        var area = await PostAsync(c, "api/areas", new { title = Unique("Avdelning ") });
        await PostAsync(c, "api/equipment", new { title = Unique("T"), areaId = area });
        await PostAsync(c, "api/equipment", new { title = Unique("T"), areaId = area });

        var areas = await c.GetFromJsonAsync<List<AreaRow>>("api/areas", Json, Ct);

        Assert.Equal(2, areas!.Single(a => a.Id == area).EquipmentCount);
    }
}
