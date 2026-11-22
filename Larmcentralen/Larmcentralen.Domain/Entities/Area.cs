namespace Larmcentralen.Domain.Entities;

public class Area
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Location { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Set while this row is in the bin (Papperskorg); such rows are hidden by query filters.</summary>
    public int? TrashBatchId { get; set; }

    public List<Equipment> Equipment { get; set; } = [];
}