namespace Larmcentralen.Domain.Entities;

/// <summary>
/// A step-by-step fix, written for alarm definitions. One solution can be shared by several
/// definitions, so editing it once updates every unit. A solution always has at least one definition.
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

    public List<AlarmDefinition> Definitions { get; set; } = [];
}
