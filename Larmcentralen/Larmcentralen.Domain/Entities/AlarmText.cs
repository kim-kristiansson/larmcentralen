using System.Text;

namespace Larmcentralen.Domain.Entities;

/// <summary>Shared rules for comparing alarm codes, texts and unit types.</summary>
public static class AlarmText
{
    /// <summary>The type units without a type belong to. Alarms on "Övrigt" units are never related across units.</summary>
    public const string OtherType = "Övrigt";

    /// <summary>The unit's type, or "Övrigt" when it has none.</summary>
    public static string TypeFor(string? equipmentType)
        => string.IsNullOrWhiteSpace(equipmentType) ? OtherType : equipmentType.Trim();

    /// <summary>Trimmed, upper case; null when empty. Codes are compared this way ("m200.0 " is M200.0).</summary>
    public static string? NormaliseCode(string? code)
        => string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();

    /// <summary>
    /// Lower case, accents removed (å → a), whitespace and punctuation removed:
    /// "Temperatur  för HÖG!" and "temperatur for hog" give the same key.
    /// </summary>
    public static string NormaliseTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        var decomposed = title.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Severity order, higher is more severe. Unknown values rank as Medel.</summary>
    public static int SeverityRank(string? severity) => severity switch
    {
        "Låg" => 0,
        "Medel" => 1,
        "Hög" => 2,
        "Kritisk" => 3,
        _ => 1
    };
}
