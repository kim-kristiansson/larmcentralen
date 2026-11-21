namespace Larmcentralen.Domain.Entities;

/// <summary>
/// An alarm on one unit. What it means (code, text, severity, description) and its solutions
/// live on its <see cref="Definition"/>, which units of the same type share.
/// The id stays the same for good: recently viewed lists, SharePoint and links use it.
/// </summary>
public class Alarm
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public int EquipmentId { get; set; }
    public Equipment Equipment { get; set; } = null!;

    public int AlarmDefinitionId { get; set; }
    public AlarmDefinition Definition { get; set; } = null!;
}
