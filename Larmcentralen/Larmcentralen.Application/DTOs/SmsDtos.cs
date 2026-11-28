namespace Larmcentralen.Application.DTOs;

/// <summary>
/// Settings for incoming alarm SMS (admin only). Numbers are in international form (+46…).
/// <see cref="LastSms"/> is null until the first SMS has come in.
/// The 46elks password is write-only: only <see cref="HasElksPassword"/> says whether one is saved.
/// <see cref="AlertRecipient"/> gets an alert SMS for Hög and Kritisk alarms; null sends none.
/// </summary>
public record SmsSettingsDto(string? ElksNumber, List<string> SenderNumbers, LastSmsDto? LastSms, DateTime? UpdatedAt,
    LastFetchDto? LastFetch = null, string? ElksUsername = null, bool HasElksPassword = false, string? AlertRecipient = null);

/// <summary>The last SMS that came in, and what happened to it (Swedish).</summary>
public record LastSmsDto(DateTime ReceivedAt, string? From, bool Accepted, string Result);

/// <summary>
/// A change to the SMS settings. Numbers may be written any Swedish way (070-123 45 67, +4670…).
/// A field left out (null) keeps its value; "" clears the 46elks number and [] removes every sender.
/// <see cref="ElksUsername"/> and <see cref="ElksPassword"/> are the company's own 46elks API login (best a subaccount);
/// "" as username removes the saved login (then nothing is fetched or sent). <see cref="AlertRecipient"/>: "" clears it.
/// </summary>
public record UpdateSmsSettingsDto(string? ElksNumber, List<string>? SenderNumbers, string? ElksUsername = null, string? ElksPassword = null,
    string? AlertRecipient = null);

/// <summary>Whether an incoming SMS may be acted on, and if not, why (Swedish).</summary>
public record SmsCheck(bool Accepted, string? Reason);

/// <summary>When the API last asked 46elks for new SMS, and why it failed (Swedish) when it did.</summary>
public record LastFetchDto(DateTime At, string? Error);

/// <summary>What fetching from 46elks needs: the number to fetch for and where the last fetch stopped.</summary>
public record SmsFetchState(string? ElksNumber, DateTime? FetchedUntil);

/// <summary>A 46elks API login (HTTP Basic: API username and API password).</summary>
public record ElksLogin(string Username, string Password);

/// <summary>Result of "Testa anslutningen": whether 46elks accepted the login and the number is on the account (Swedish).</summary>
public record SmsConnectionTestDto(bool Ok, List<string> Messages);

/// <summary>An SMS from the alarm system that didn't match the alarm format, word for word.</summary>
public record UnrecognisedSmsDto(int Id, DateTime ReceivedAt, string? From, string Message);
