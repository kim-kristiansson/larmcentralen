using System.Text.RegularExpressions;

namespace Larmcentralen.Domain.Entities;

/// <summary>
/// Swedish phone numbers written one way, so a number typed as "070-123 45 67" matches the "+46701234567"
/// that 46elks sends.
/// </summary>
public static partial class SwedishPhoneNumber
{
    /// <summary>
    /// The number in international form (+46…), or null when it isn't a Swedish phone number.
    /// Accepts 07x…, +467x…, 00467x… and 467x…, with or without spaces, dashes, dots and parentheses.
    /// </summary>
    public static string? Normalise(string? number)
    {
        if (string.IsNullOrWhiteSpace(number)) return null;

        var digits = Separators().Replace(number.Trim(), "");
        if (digits.StartsWith("00")) digits = "+" + digits[2..];
        else if (digits.StartsWith('0')) digits = "+46" + digits[1..];
        else if (digits.StartsWith("46")) digits = "+" + digits;

        // "+46 (0)70…" and "+4607…": the trunk 0 doesn't belong after the country code.
        if (digits.StartsWith("+460")) digits = "+46" + digits[4..];

        return Valid().IsMatch(digits) ? digits : null;
    }

    /// <summary>True when both are the same Swedish number, however they are written.</summary>
    public static bool SameNumber(string? a, string? b)
        => Normalise(a) is { } x && x == Normalise(b);

    [GeneratedRegex(@"[\s\-().\/]")]
    private static partial Regex Separators();

    // After +46: 7 to 9 digits, never starting with 0 (070-123 45 67 is +46 70 123 45 67).
    [GeneratedRegex(@"^\+46[1-9]\d{6,8}$")]
    private static partial Regex Valid();
}
