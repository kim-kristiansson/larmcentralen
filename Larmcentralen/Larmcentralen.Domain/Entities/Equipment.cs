namespace Larmcentralen.Domain.Entities;

public class Equipment
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Category { get; set; }
    /// <summary>What kind of unit this is, e.g. "Härdugn" or "Tvätt". Used to group units in lists.</summary>
    public string? EquipmentType { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Set while this row is in the bin (Papperskorg); such rows are hidden by query filters.</summary>
    public int? TrashBatchId { get; set; }

    public int AreaId { get; set; }
    public Area Area { get; set; } = null!;

    public List<Alarm> Alarms { get; set; } = [];
}