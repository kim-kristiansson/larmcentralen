using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Larmcentralen.Api.Tests;

/// <summary>Admins keep the contact people and their roles. Names and numbers are invented test data.</summary>
public sealed class ContactTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private record RoleRow(int Id, string Name, int ContactCount);
    private record ContactRow(int Id, string Name, string Phone, int RoleId, string RoleName, string? Note);
    private record Problem(string? Detail, List<string>? Contacts);

    private HttpClient Admin() => factory.ClientWithKey(ApiFactory.AdminKey);

    private static string Unique(string prefix) => $"{prefix} {Random.Shared.Next(100000, 999999)}";

    private static async Task<T> CreatedAsync<T>(HttpClient c, string url, object body)
    {
        using var response = await c.PostAsJsonAsync(url, body, Ct);
        Assert.True(response.StatusCode == HttpStatusCode.Created,
            $"{url}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return (await response.Content.ReadFromJsonAsync<T>(Json, Ct))!;
    }

    private static async Task<Problem?> ProblemAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<Problem>(Json, Ct);

    [Fact]
    public async Task Admin_can_add_rename_and_remove_a_role()
    {
        using var c = Admin();
        var name = Unique("Teamledare");
        var role = await CreatedAsync<RoleRow>(c, "api/contact-roles", new { name = $"  {name} " });
        Assert.Equal((name, 0), (role.Name, role.ContactCount));

        var renamed = Unique("Skiftledare");
        using (var put = await c.PutAsJsonAsync($"api/contact-roles/{role.Id}", new { name = renamed }, Ct))
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var roles = await c.GetFromJsonAsync<List<RoleRow>>("api/contact-roles", Json, Ct);
        Assert.Contains(roles!, r => r.Id == role.Id && r.Name == renamed);

        using (var delete = await c.DeleteAsync($"api/contact-roles/{role.Id}", Ct))
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        roles = await c.GetFromJsonAsync<List<RoleRow>>("api/contact-roles", Json, Ct);
        Assert.DoesNotContain(roles!, r => r.Id == role.Id);
    }

    [Fact]
    public async Task A_role_name_is_required_and_unique_in_any_case()
    {
        using var c = Admin();
        using (var empty = await c.PostAsJsonAsync("api/contact-roles", new { name = "   " }, Ct))
        {
            Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
            Assert.Equal("Ange rollens namn.", (await ProblemAsync(empty))?.Detail);
        }

        var name = Unique("Underhåll el");
        var first = await CreatedAsync<RoleRow>(c, "api/contact-roles", new { name });
        using (var same = await c.PostAsJsonAsync("api/contact-roles", new { name = name.ToUpperInvariant() }, Ct))
            Assert.Equal(HttpStatusCode.Conflict, same.StatusCode);

        // Renaming to another role's name is refused too; keeping its own name is fine.
        var other = await CreatedAsync<RoleRow>(c, "api/contact-roles", new { name = Unique("Underhåll mek") });
        using (var taken = await c.PutAsJsonAsync($"api/contact-roles/{other.Id}", new { name }, Ct))
            Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        using (var own = await c.PutAsJsonAsync($"api/contact-roles/{first.Id}", new { name }, Ct))
            Assert.Equal(HttpStatusCode.OK, own.StatusCode);
    }

    [Fact]
    public async Task Admin_can_add_change_and_remove_a_contact_and_the_number_is_stored_as_plus_46()
    {
        using var c = Admin();
        var role = await CreatedAsync<RoleRow>(c, "api/contact-roles", new { name = Unique("Teamledare") });
        var other = await CreatedAsync<RoleRow>(c, "api/contact-roles", new { name = Unique("Elektriker") });

        var contact = await CreatedAsync<ContactRow>(c, "api/contacts",
            new { name = " Testa Testsson ", phone = "070-000 12 34", roleId = role.Id, note = " vardagar 07–16 " });
        Assert.Equal(("Testa Testsson", "+46700001234", role.Id, role.Name, "vardagar 07–16"),
            (contact.Name, contact.Phone, contact.RoleId, contact.RoleName, contact.Note));

        using (var put = await c.PutAsJsonAsync($"api/contacts/{contact.Id}",
                   new { name = "Prova Provsson", phone = "+46 70 000 56 78", roleId = other.Id, note = "" }, Ct))
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var changed = await c.GetFromJsonAsync<ContactRow>($"api/contacts/{contact.Id}", Json, Ct);
        Assert.Equal(("Prova Provsson", "+46700005678", other.Id, other.Name, (string?)null),
            (changed!.Name, changed.Phone, changed.RoleId, changed.RoleName, changed.Note));

        var roles = await c.GetFromJsonAsync<List<RoleRow>>("api/contact-roles", Json, Ct);
        Assert.Equal((0, 1), (roles!.Single(r => r.Id == role.Id).ContactCount, roles!.Single(r => r.Id == other.Id).ContactCount));

        using (var delete = await c.DeleteAsync($"api/contacts/{contact.Id}", Ct))
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        using (var gone = await c.GetAsync($"api/contacts/{contact.Id}", Ct))
            Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        using (var again = await c.DeleteAsync($"api/contacts/{contact.Id}", Ct))
            Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task Removing_a_role_in_use_is_409_and_names_the_people()
    {
        using var c = Admin();
        var role = await CreatedAsync<RoleRow>(c, "api/contact-roles", new { name = Unique("Jour") });
        var erik = await CreatedAsync<ContactRow>(c, "api/contacts", new { name = "Erik Exempel", phone = "0700001111", roleId = role.Id });
        await CreatedAsync<ContactRow>(c, "api/contacts", new { name = "Anna Exempel", phone = "0700002222", roleId = role.Id });

        using (var delete = await c.DeleteAsync($"api/contact-roles/{role.Id}", Ct))
        {
            Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
            var problem = await ProblemAsync(delete);
            Assert.Equal(["Anna Exempel", "Erik Exempel"], problem?.Contacts);
            Assert.Contains("Anna Exempel och Erik Exempel", problem?.Detail);
        }

        // Once nobody has it, it can go.
        using (var put = await c.PutAsJsonAsync($"api/contacts/{erik.Id}", new { name = erik.Name, phone = erik.Phone, roleId = role.Id }, Ct))
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        foreach (var contact in (await c.GetFromJsonAsync<List<ContactRow>>("api/contacts", Json, Ct))!.Where(x => x.RoleId == role.Id))
            using (await c.DeleteAsync($"api/contacts/{contact.Id}", Ct)) { }
        using (var delete = await c.DeleteAsync($"api/contact-roles/{role.Id}", Ct))
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [Theory]
    [InlineData("", "0700001234", true, "Ange namnet.")]
    [InlineData("Testa Testsson", "  ", true, "Ange telefonnumret.")]
    [InlineData("Testa Testsson", "12345", true, "Telefonnumret ser inte ut som ett svenskt nummer. Skriv det till exempel 070-123 45 67.")]
    [InlineData("Testa Testsson", "0700001234", false, "Välj en roll.")]
    public async Task A_contact_needs_a_name_a_swedish_number_and_a_role(string name, string phone, bool withRole, string error)
    {
        using var c = Admin();
        var roleId = withRole ? (await CreatedAsync<RoleRow>(c, "api/contact-roles", new { name = Unique("Roll") })).Id : 0;

        using var response = await c.PostAsJsonAsync("api/contacts", new { name, phone, roleId }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(error, (await ProblemAsync(response))?.Detail);
    }

    [Fact]
    public async Task A_contact_with_a_role_that_does_not_exist_is_400()
    {
        using var c = Admin();
        using var response = await c.PostAsJsonAsync("api/contacts", new { name = "Testa Testsson", phone = "0700001234", roleId = 999999 }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Rollen finns inte längre. Välj en annan.", (await ProblemAsync(response))?.Detail);
    }

    [Theory]
    [InlineData("GET", "api/contacts")]
    [InlineData("GET", "api/contacts/1")]
    [InlineData("GET", "api/contact-roles")]
    [InlineData("POST", "api/contacts")]
    [InlineData("PUT", "api/contacts/1")]
    [InlineData("DELETE", "api/contacts/1")]
    [InlineData("POST", "api/contact-roles")]
    [InlineData("PUT", "api/contact-roles/1")]
    [InlineData("DELETE", "api/contact-roles/1")]
    public async Task Read_key_can_neither_see_nor_change_contacts(string method, string url)
    {
        using var client = factory.ClientWithKey(ApiFactory.ReadKey);
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method is "POST" or "PUT") request.Content = JsonContent.Create(new { name = "x", phone = "0700001234", roleId = 1 });
        using var response = await client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>A fresh invented number in three ways of writing it: 0701234567, +46701234567 and 070-123 45 67.</summary>
    private static (string Plain, string International, string Spaced) NewNumber()
    {
        var n = Random.Shared.Next(10000, 99999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return ($"07000{n}", $"+467000{n}", $"070-00{n[0]} {n[1..3]} {n[3..]}");
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 0)]
    public async Task The_same_number_twice_is_409_however_it_is_written(int firstWay, int secondWay)
    {
        using var c = Admin();
        var role = await CreatedAsync<RoleRow>(c, "api/contact-roles", new { name = Unique("Roll") });
        var number = NewNumber();
        string[] ways = [number.Plain, number.International, number.Spaced];
        var first = await CreatedAsync<ContactRow>(c, "api/contacts", new { name = Unique("Maria Exempel"), phone = ways[firstWay], roleId = role.Id });

        using var response = await c.PostAsJsonAsync("api/contacts", new { name = Unique("Någon Annan"), phone = ways[secondWay], roleId = role.Id }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal($"Numret används redan av {first.Name}.", (await ProblemAsync(response))?.Detail);
    }

    [Fact]
    public async Task The_same_name_twice_is_409_ignoring_case_and_spacing()
    {
        using var c = Admin();
        var role = await CreatedAsync<RoleRow>(c, "api/contact-roles", new { name = Unique("Roll") });
        var name = Unique("Åsa Exempel");
        var first = await CreatedAsync<ContactRow>(c, "api/contacts", new { name = $"  {name.Replace(" ", "   ")} ", phone = NewNumber().Plain, roleId = role.Id });
        Assert.Equal(name, first.Name);

        using var response = await c.PostAsJsonAsync("api/contacts", new { name = name.ToUpperInvariant(), phone = NewNumber().Plain, roleId = role.Id }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal($"Det finns redan en kontakt som heter {name}.", (await ProblemAsync(response))?.Detail);
    }

    [Fact]
    public async Task Editing_into_another_contacts_name_or_number_is_409_but_keeping_your_own_is_fine()
    {
        using var c = Admin();
        var role = await CreatedAsync<RoleRow>(c, "api/contact-roles", new { name = Unique("Roll") });
        var takenNumber = NewNumber();
        var taken = await CreatedAsync<ContactRow>(c, "api/contacts", new { name = Unique("Erik Exempel"), phone = takenNumber.Plain, roleId = role.Id });
        var mine = NewNumber();
        var me = await CreatedAsync<ContactRow>(c, "api/contacts", new { name = Unique("Sara Exempel"), phone = mine.Plain, roleId = role.Id });

        using (var sameName = await c.PutAsJsonAsync($"api/contacts/{me.Id}", new { name = taken.Name.ToLowerInvariant(), phone = mine.Plain, roleId = role.Id }, Ct))
        {
            Assert.Equal(HttpStatusCode.Conflict, sameName.StatusCode);
            Assert.Equal($"Det finns redan en kontakt som heter {taken.Name}.", (await ProblemAsync(sameName))?.Detail);
        }
        using (var sameNumber = await c.PutAsJsonAsync($"api/contacts/{me.Id}", new { name = me.Name, phone = takenNumber.Spaced, roleId = role.Id }, Ct))
        {
            Assert.Equal(HttpStatusCode.Conflict, sameNumber.StatusCode);
            Assert.Equal($"Numret används redan av {taken.Name}.", (await ProblemAsync(sameNumber))?.Detail);
        }
        // Its own name and number, written another way, is not a clash with itself.
        using (var own = await c.PutAsJsonAsync($"api/contacts/{me.Id}", new { name = me.Name.ToUpperInvariant(), phone = mine.International, roleId = role.Id, note = "kvällar" }, Ct))
            Assert.Equal(HttpStatusCode.OK, own.StatusCode);
    }

    [Fact]
    public async Task A_role_name_is_stored_with_spaces_tidied_and_rename_checks_it_the_same_way()
    {
        using var c = Admin();
        var name = Unique("Underhåll el");
        var role = await CreatedAsync<RoleRow>(c, "api/contact-roles", new { name = $"  {name.Replace(" ", "   ")}  " });
        Assert.Equal(name, role.Name);

        using (var again = await c.PostAsJsonAsync("api/contact-roles", new { name = name.Replace(" ", "  ").ToUpperInvariant() }, Ct))
        {
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
            Assert.Equal($"Det finns redan en roll som heter {name}.", (await ProblemAsync(again))?.Detail);
        }

        var other = await CreatedAsync<RoleRow>(c, "api/contact-roles", new { name = Unique("Underhåll mek") });
        using var rename = await c.PutAsJsonAsync($"api/contact-roles/{other.Id}", new { name = $" {name.ToLowerInvariant().Replace(" ", "    ")} " }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, rename.StatusCode);
        Assert.Equal($"Det finns redan en roll som heter {name}.", (await ProblemAsync(rename))?.Detail);
    }
}
