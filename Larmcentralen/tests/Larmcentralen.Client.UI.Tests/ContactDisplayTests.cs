using Larmcentralen.Client.Core.Models;
using Larmcentralen.Client.UI.Services;

namespace Larmcentralen.Client.UI.Tests;

/// <summary>How the Kontakter page shows and filters people. Names and numbers are invented.</summary>
public class ContactDisplayTests
{
    private static readonly List<Contact> People =
    [
        new(1, "Östen Testsson", "+46700001111", 2, "Underhåll el", null),
        new(2, "Anna Exempel", "+46700002222", 1, "Teamledare", "vardagar 07–16"),
        new(3, "Erik Prov", "+46700003333", 2, "Underhåll el", null),
    ];

    [Theory]
    [InlineData("Anna Exempel", "AE")]
    [InlineData("anna-karin  von prov", "AP")]
    [InlineData("Östen", "Ö")]
    [InlineData("  ", "")]
    [InlineData(null, "")]
    public void Initials_are_the_first_and_last_names_first_letters(string? name, string expected)
        => Assert.Equal(expected, ContactDisplay.Initials(name));

    [Theory]
    [InlineData("+46700001234", "070-000 12 34")]
    [InlineData("+4681234567", "081234567")]
    [InlineData("+4512345678", "+4512345678")]
    [InlineData("", "")]
    public void Numbers_are_shown_the_swedish_way(string stored, string expected)
        => Assert.Equal(expected, ContactDisplay.Phone(stored));

    [Fact]
    public void Without_a_filter_everyone_is_listed_by_name_with_swedish_order()
        => Assert.Equal([2, 3, 1], ContactDisplay.Filter(People, null, null).Select(c => c.Id));

    [Theory]
    [InlineData("anna", new[] { 2 })]
    [InlineData("underhåll", new[] { 3, 1 })]
    [InlineData("el erik", new[] { 3 })]
    [InlineData("ingen", new int[0])]
    public void Search_matches_name_or_role_and_every_word_must_match(string text, int[] expected)
        => Assert.Equal(expected, ContactDisplay.Filter(People, text, null).Select(c => c.Id));

    // The API's own messages (ContactService; ContactTests pins their wording on the API side).
    [Theory]
    [InlineData("Ange namnet.", "Name")]
    [InlineData("Namnet får vara högst 100 tecken.", "Name")]
    [InlineData("Ange telefonnumret.", "Phone")]
    [InlineData("Telefonnumret ser inte ut som ett svenskt nummer. Skriv det till exempel 070-123 45 67.", "Phone")]
    [InlineData("Välj en roll.", "RoleId")]
    [InlineData("Rollen finns inte längre. Välj en annan.", "RoleId")]
    [InlineData("Anmärkningen får vara högst 200 tecken.", "Note")]
    [InlineData("Det finns redan en kontakt som heter Anna Exempel.", "Name")]
    [InlineData("Numret används redan av Anna Exempel.", "Phone")]
    [InlineData("Någon annan ändrade samtidigt. Ladda om sidan och försök igen.", null)]
    [InlineData(null, null)]
    public void A_reason_from_the_api_goes_under_its_field(string? detail, string? field)
        => Assert.Equal(field, ContactDisplay.FieldFor(detail));

    [Fact]
    public void The_role_filter_and_the_search_work_together()
    {
        Assert.Equal([3, 1], ContactDisplay.Filter(People, "", 2).Select(c => c.Id));
        Assert.Equal([1], ContactDisplay.Filter(People, "östen", 2).Select(c => c.Id));
        Assert.Empty(ContactDisplay.Filter(People, "anna", 2));
    }

    [Theory]
    [InlineData("0700002222")]
    [InlineData("+46700002222")]
    [InlineData("070-000 22 22")]
    [InlineData("+46 (0)70 000 22 22")]
    [InlineData("0046700002222")]
    public void Any_way_of_writing_a_number_is_the_same_number(string written)
        => Assert.Equal("+46700002222", ContactDisplay.NormalisePhone(written));

    [Theory]
    [InlineData("12345")]
    [InlineData("")]
    [InlineData("070-000 22 22 22 22")]
    public void Something_that_is_not_a_swedish_number_is_null(string written)
        => Assert.Null(ContactDisplay.NormalisePhone(written));

    [Theory]
    [InlineData(null, "  anna   EXEMPEL ", "0709999999", "Name", "Det finns redan en kontakt som heter Anna Exempel.")]
    [InlineData(null, "Ny Person", "070-000 22 22", "Phone", "Numret används redan av Anna Exempel.")]
    [InlineData(3, "Anna Exempel", "0709999999", "Name", "Det finns redan en kontakt som heter Anna Exempel.")]
    [InlineData(3, "Erik Prov", "+46700002222", "Phone", "Numret används redan av Anna Exempel.")]
    public void A_name_or_number_someone_else_has_is_a_duplicate(int? id, string name, string phone, string field, string message)
        => Assert.Equal((field, message), ContactDisplay.FindDuplicate(People, id, name, phone));

    [Fact]
    public void A_contact_keeping_its_own_name_and_number_is_not_a_duplicate()
        => Assert.Null(ContactDisplay.FindDuplicate(People, 2, " anna exempel", "070-000 22 22"));

    [Fact]
    public void A_role_name_matches_ignoring_case_and_spacing_but_not_itself()
    {
        List<ContactRole> roles = [new(1, "Teamledare", 1), new(2, "Underhåll el", 2)];
        Assert.Equal(2, ContactDisplay.FindRole(roles, null, "  UNDERHÅLL   el ")?.Id);
        Assert.Null(ContactDisplay.FindRole(roles, 2, "underhåll el"));
        Assert.Null(ContactDisplay.FindRole(roles, null, "Underhåll mek"));
    }
}
