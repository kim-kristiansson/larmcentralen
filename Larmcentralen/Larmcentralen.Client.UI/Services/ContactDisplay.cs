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
        _ when detail.Contains("telefonnumret", StringComparison.OrdinalIgnoreCase) || detail.StartsWith("Numret", StringComparison.Ordinal) => "Phone",
        _ when detail.StartsWith("Välj en roll", StringComparison.Ordinal) || detail.StartsWith("Rollen", StringComparison.Ordinal) => "RoleId",
        _ when detail.StartsWith("Anmärkningen", StringComparison.Ordinal) => "Note",
        _ when detail.StartsWith("Ange namnet", StringComparison.Ordinal) || detail.StartsWith("Namnet", StringComparison.Ordinal)
                   || detail.StartsWith("Det finns redan en kontakt", StringComparison.Ordinal) => "Name",
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

    /// <summary>
    /// The same check the server makes before saving, so the admin hears about it at once: another contact with the
    /// same name (however spaced or cased) or the same number (however written). Field and message as the server
    /// gives them; null when there is none. <paramref name="id"/> is the contact being edited (null for a new one).
    /// </summary>
    public static (string Field, string Message)? FindDuplicate(IEnumerable<Contact> contacts, int? id, string? name, string? phone)
    {
        var others = contacts.Where(c => c.Id != id).ToList();
        if (others.FirstOrDefault(c => SameName(c.Name, name)) is { } sameName)
            return ("Name", $"Det finns redan en kontakt som heter {CleanName(sameName.Name)}.");
        if (NormalisePhone(phone) is { } number && others.FirstOrDefault(c => NormalisePhone(c.Phone) == number) is { } sameNumber)
            return ("Phone", $"Numret används redan av {CleanName(sameNumber.Name)}.");
        return null;
    }

    /// <summary>The role with the same name (however spaced or cased), other than <paramref name="id"/>; null when none.</summary>
    public static ContactRole? FindRole(IEnumerable<ContactRole> roles, int? id, string? name)
        => roles.FirstOrDefault(r => r.Id != id && SameName(r.Name, name));

    /// <summary>Trimmed, with runs of spaces inside made one, as the server stores names.</summary>
    public static string CleanName(string? text) => string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static bool SameName(string? a, string? b)
        => CleanName(a) is { Length: > 0 } x && string.Equals(x, CleanName(b), StringComparison.CurrentCultureIgnoreCase);

    /// <summary>
    /// A Swedish number in +46 form, the way the server stores it ("070-123 45 67", "0701234567" and "+46 70 123 45 67"
    /// are all "+46701234567"); null when it isn't one. The server checks again: this is only for an early message.
    /// </summary>
    public static string? NormalisePhone(string? number)
    {
        if (string.IsNullOrWhiteSpace(number)) return null;
        var digits = new string(number.Trim().Where(ch => !(char.IsWhiteSpace(ch) || ch is '-' or '(' or ')' or '.' or '/')).ToArray());
        if (digits.StartsWith("00", StringComparison.Ordinal)) digits = "+" + digits[2..];
        else if (digits.StartsWith('0')) digits = "+46" + digits[1..];
        else if (digits.StartsWith("46", StringComparison.Ordinal)) digits = "+" + digits;
        if (digits.StartsWith("+460", StringComparison.Ordinal)) digits = "+46" + digits[4..];
        var rest = digits.StartsWith("+46", StringComparison.Ordinal) ? digits[3..] : "";
        return rest.Length is >= 7 and <= 9 && rest[0] != '0' && rest.All(char.IsAsciiDigit) ? digits : null;
    }
}
