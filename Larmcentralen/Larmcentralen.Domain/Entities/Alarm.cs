namespace Larmcentralen.Domain.Entities;

/// <summary>
/// An alarm on one unit, with its own text, severity and description. Its <see cref="AlarmCode"/> (when it has one)
/// is shared with the same alarm on other units. The id stays the same for good: recently viewed lists, SharePoint
/// and links use it.
/// </summary>
public class Alarm
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Severity { get; set; } = "Låg";
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Set while this row is in the bin (Papperskorg); such rows are hidden by query filters.</summary>
    public int? TrashBatchId { get; set; }

    public int EquipmentId { get; set; }
    public Equipment Equipment { get; set; } = null!;

    /// <summary>Null for alarm rows without a PLC code.</summary>
    public int? AlarmCodeId { get; set; }
    public AlarmCode? AlarmCode { get; set; }

    public List<Solution> Solutions { get; set; } = [];
}
