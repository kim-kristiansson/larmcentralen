using System.Globalization;
using System.Text;

namespace Larmcentralen.Domain.Entities;

/// <summary>
/// What an alarm means, shared by the units that raise it. Alarms on units of the same
/// <see cref="EquipmentType"/> with the same code and the same text (ignoring case, accents,
/// spacing and punctuation) share one definition, and with it their solutions. Codes are not
/// global: the same code with another text, or on another type of unit, is another definition.
/// </summary>
public class AlarmDefinition
{
    /// <summary>The type units without a type belong to. Its definitions are never shared between units.</summary>
    public const string OtherType = "Övrigt";

    public int Id { get; set; }

    /// <summary>The unit type, e.g. "Härdugn". "Övrigt" for units without a type.</summary>
    public string EquipmentType { get; set; } = OtherType;

    /// <summary>
    /// 0 when every unit of <see cref="EquipmentType"/> may share the definition. Otherwise the id of the
    /// one unit that owns it: always for "Övrigt", and for a unit split off with its own definition.
    /// </summary>
    public int ScopeEquipmentId { get; set; }

    public string? AlarmCode { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>The matching key for <see cref="Title"/>, see <see cref="NormaliseTitle"/>.</summary>
    public string NormalizedTitle { get; set; } = string.Empty;

    public string Severity { get; set; } = "Låg";
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public List<Alarm> Alarms { get; set; } = [];
    public List<Solution> Solutions { get; set; } = [];

    /// <summary>The type a unit's definitions are filed under: its own type, or "Övrigt".</summary>
    public static string TypeFor(Equipment equipment) => TypeFor(equipment.EquipmentType);

    public static string TypeFor(string? equipmentType)
        => string.IsNullOrWhiteSpace(equipmentType) ? OtherType : equipmentType.Trim();

    /// <summary>The scope a new definition for this unit gets: shared by its type, or its own for "Övrigt".</summary>
    public static int DefaultScopeFor(Equipment equipment)
        => TypeFor(equipment) == OtherType ? equipment.Id : 0;

    /// <summary>Trimmed code, null when empty. Codes are compared as written (case-insensitive is not needed: PLC codes are upper case).</summary>
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
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        }
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
