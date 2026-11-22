namespace Larmcentralen.Application.DTOs;

/// <summary>
/// Settings for incoming alarm SMS (admin only). Numbers are in international form (+46…).
/// <see cref="LastSms"/> is null until the first SMS has come in.
/// </summary>
public record SmsSettingsDto(string? ElksNumber, List<string> SenderNumbers, LastSmsDto? LastSms, DateTime? UpdatedAt,
    LastFetchDto? LastFetch = null);

/// <summary>The last SMS that came in, and what happened to it (Swedish).</summary>
public record LastSmsDto(DateTime ReceivedAt, string? From, bool Accepted, string Result);

/// <summary>
/// A change to the SMS settings. Numbers may be written any Swedish way (070-123 45 67, +4670…).
/// A field left out (null) keeps its value; "" clears the 46elks number and [] removes every sender.
/// </summary>
public record UpdateSmsSettingsDto(string? ElksNumber, List<string>? SenderNumbers);

/// <summary>Whether an incoming SMS may be acted on, and if not, why (Swedish).</summary>
public record SmsCheck(bool Accepted, string? Reason);

/// <summary>When the API last asked 46elks for new SMS, and why it failed (Swedish) when it did.</summary>
public record LastFetchDto(DateTime At, string? Error);

/// <summary>What fetching from 46elks needs: the number to fetch for and where the last fetch stopped.</summary>
public record SmsFetchState(string? ElksNumber, DateTime? FetchedUntil);
