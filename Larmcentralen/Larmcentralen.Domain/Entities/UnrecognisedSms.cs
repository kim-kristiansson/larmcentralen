namespace Larmcentralen.Domain.Entities;

/// <summary>
/// An SMS from the alarm system that didn't match the alarm format, kept word for word so a new format can be
/// recognised later. Only the latest <see cref="MaxKept"/> are kept; admins remove them under SMS-larm.
/// </summary>
public class UnrecognisedSms
{
    public const int MaxKept = 200;

    public int Id { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    /// <summary>The sender, as 46elks gave it.</summary>
    public string? From { get; set; }

    /// <summary>The exact text.</summary>
    public string Message { get; set; } = string.Empty;
}
