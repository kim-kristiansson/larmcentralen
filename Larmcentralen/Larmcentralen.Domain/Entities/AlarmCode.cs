namespace Larmcentralen.Domain.Entities;

/// <summary>
/// A PLC alarm code such as "M200.0", listed once however many units use it. It has no text, type or area of its
/// own: each unit's alarm with the code has its own text. Alarms with the same code on units of the same type are
/// related (the same fault on another furnace). New codes can be added from the app.
/// </summary>
public class AlarmCode
{
    public int Id { get; set; }

    /// <summary>Unique, stored trimmed and upper case (see <see cref="AlarmText.NormaliseCode"/>).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Optional note about the code itself, not alarm text.</summary>
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Alarm> Alarms { get; set; } = [];
}
