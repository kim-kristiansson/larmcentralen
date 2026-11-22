namespace Larmcentralen.Domain.Entities;

/// <summary>
/// A step-by-step fix. One solution can be shared by several alarms (the same fault on several
/// units), so editing it once updates every unit. A solution always has at least one alarm.
/// </summary>
public class Solution
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Content { get; set; }              // HTML from the editor
    public int SortOrder { get; set; } = 10;
    public string? EstimatedTime { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Set while this row is in the bin (Papperskorg); such rows are hidden by query filters.</summary>
    public int? TrashBatchId { get; set; }

    public List<Alarm> Alarms { get; set; } = [];
}
