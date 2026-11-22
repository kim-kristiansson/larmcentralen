namespace Larmcentralen.Domain.Entities;

/// <summary>
/// One removal in the bin (Papperskorg): an avdelning or a unit, together with everything that went with it
/// (its units, their alarms and the solutions only those alarms used). The rows themselves stay in their tables,
/// marked with this batch's id, and are hidden everywhere by query filters. Restoring clears the mark; after
/// <see cref="RetentionDays"/> days the rows are erased for good.
/// </summary>
public class TrashBatch
{
    /// <summary>How long a removal can be undone.</summary>
    public const int RetentionDays = 30;

    public int Id { get; set; }

    /// <summary><see cref="KindArea"/> or <see cref="KindEquipment"/>.</summary>
    public string Kind { get; set; } = KindEquipment;

    /// <summary>The id of the removed avdelning or unit.</summary>
    public int ItemId { get; set; }

    /// <summary>Its name when it was removed, for the bin's list.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>For a unit: the avdelning it belonged to.</summary>
    public string? AreaTitle { get; set; }

    public int EquipmentCount { get; set; }
    public int AlarmCount { get; set; }
    public int SolutionCount { get; set; }

    public DateTime DeletedAt { get; set; } = DateTime.UtcNow;

    public const string KindArea = "Area";
    public const string KindEquipment = "Equipment";
}
