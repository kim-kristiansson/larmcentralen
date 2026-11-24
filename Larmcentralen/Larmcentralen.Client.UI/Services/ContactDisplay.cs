using Larmcentralen.Client.Core.Models;

namespace Larmcentralen.Client.UI.Services;

/// <summary>How the Kontakter page shows and filters contact people.</summary>
public static class ContactDisplay
{
    /// <summary>"Anna Berg" → "AB", "Anna" → "A", "anna-karin berg" → "AB". Empty for an empty name.</summary>
    public static string Initials(string? name)
    {
        var words = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length switch
        {
            0 => "",
            1 => words[0][..1].ToUpper(Format.Swedish),
            _ => (words[0][..1] + words[^1][..1]).ToUpper(Format.Swedish)
        };
    }

    /// <summary>
    /// A stored number (+46…) the way Swedes write it: "+46701234567" → "070-123 45 67". Other Swedish numbers get
    /// the trunk 0 back ("+4681234567" → "081234567"); anything else is shown as it is.
    /// </summary>
    public static string Phone(string? number)
    {
        if (string.IsNullOrWhiteSpace(number)) return "";
        if (!number.StartsWith("+46", StringComparison.Ordinal)) return number;
        var national = "0" + number[3..];
        return national.Length == 10 && national.StartsWith("07", StringComparison.Ordinal)
            ? $"{national[..3]}-{national[3..6]} {national[6..8]} {national[8..]}"
            : national;
    }

    /// <summary>
    /// Which field of the contact form a reason from the API is about ("Name", "RoleId", "Phone" or "Note", the form's
    /// property names), so it can be shown under that field; null for anything else.
    /// </summary>
    public static string? FieldFor(string? detail) => detail switch
    {
        null => null,
        _ when detail.Contains("telefonnumret", StringComparison.OrdinalIgnoreCase) => "Phone",
        _ when detail.StartsWith("Välj en roll", StringComparison.Ordinal) || detail.StartsWith("Rollen", StringComparison.Ordinal) => "RoleId",
        _ when detail.StartsWith("Anmärkningen", StringComparison.Ordinal) => "Note",
        _ when detail.StartsWith("Ange namnet", StringComparison.Ordinal) || detail.StartsWith("Namnet", StringComparison.Ordinal) => "Name",
        _ => null
    };

    /// <summary>
    /// Contacts whose name or role contains every word typed (any case), with the role picked (null: all roles),
    /// by name.
    /// </summary>
    public static IReadOnlyList<Contact> Filter(IEnumerable<Contact> contacts, string? text, int? roleId)
    {
        var terms = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return contacts
            .Where(c => roleId is null || c.RoleId == roleId)
            .Where(c => terms.All(t => c.Name.Contains(t, StringComparison.CurrentCultureIgnoreCase)
                                       || c.RoleName.Contains(t, StringComparison.CurrentCultureIgnoreCase)))
            .OrderBy(c => c.Name, StringComparer.Create(Format.Swedish, ignoreCase: true))
            .ToList();
    }
}
