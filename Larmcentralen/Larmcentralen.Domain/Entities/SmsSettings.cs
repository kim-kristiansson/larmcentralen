namespace Larmcentralen.Domain.Entities;

/// <summary>
/// Settings for incoming alarm SMS (one row, <see cref="SingletonId"/>). The plant's alarm system (OCSS) sends its
/// alarms by SMS to a 46elks number, and 46elks passes them on to the API's webhook. Only SMS from one of
/// <see cref="SenderNumbers"/> to <see cref="ElksNumber"/> are acted on. Numbers are stored in international form
/// (<see cref="SwedishPhoneNumber.Normalise"/>).
///
/// Not an alarm log: only the last SMS is kept, so an admin can see that SMS arrive and why one was turned away.
/// </summary>
public class SmsSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>The 46elks number the alarms are sent to. Null until set: then every SMS is turned away.</summary>
    public string? ElksNumber { get; set; }

    /// <summary>The numbers the alarm system sends from. Empty: every SMS is turned away.</summary>
    public List<string> SenderNumbers { get; set; } = [];

    public DateTime? UpdatedAt { get; set; }

    /// <summary>When the last SMS came in.</summary>
    public DateTime? LastSmsAt { get; set; }

    /// <summary>Who sent it, as 46elks gave it.</summary>
    public string? LastSmsFrom { get; set; }

    /// <summary>What happened to it, in Swedish (shown to admins).</summary>
    public string? LastSmsResult { get; set; }

    /// <summary>Whether it was accepted (from a known sender, to the right number).</summary>
    public bool LastSmsAccepted { get; set; }

    /// <summary>
    /// Fetching from 46elks' SMS history: incoming SMS created after this (UTC, 46elks' clock) are new. Null until
    /// the first fetch, which only sets it, so old SMS in the history are never acted on.
    /// </summary>
    public DateTime? FetchedUntil { get; set; }

    /// <summary>When the API last asked 46elks for new SMS.</summary>
    public DateTime? LastFetchAt { get; set; }

    /// <summary>Why the last fetch failed (Swedish), or null when it worked.</summary>
    public string? LastFetchError { get; set; }
}
