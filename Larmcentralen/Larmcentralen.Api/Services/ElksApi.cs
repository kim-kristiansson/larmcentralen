using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using Larmcentralen.Application.DTOs;
using Larmcentralen.Application.Interfaces;
using Microsoft.AspNetCore.DataProtection;

namespace Larmcentralen.Api.Services;

/// <summary>An incoming SMS from 46elks' SMS history. <see cref="Created"/> is UTC.</summary>
public sealed record ElksSms(string Id, DateTime Created, string? From, string? To, string? Message);

/// <summary>A number on the 46elks account (GET /a1/numbers).</summary>
public sealed record ElksNumber(string Number, bool Active, IReadOnlyList<string> Capabilities, string? SmsUrl);

/// <summary>46elks said no, or could not be reached. The message is Swedish, for the admin page.</summary>
public sealed class ElksException(string message, bool transient = false) : Exception(message)
{
    /// <summary>Worth trying again (5xx or no answer). 4xx never is: the request itself must change.</summary>
    public bool Transient { get; } = transient;
}

/// <summary>The parts of the 46elks REST API (HTTP Basic auth, https://api.46elks.com/a1) the API uses. Replaced in tests.</summary>
public interface IElksApi
{
    /// <summary>
    /// Incoming SMS to <paramref name="to"/> created after <paramref name="after"/>, oldest first, from the SMS
    /// history. Reads back through at most <paramref name="maxPages"/> pages of 100.
    /// </summary>
    Task<List<ElksSms>> GetIncomingAsync(ElksLogin login, string to, DateTime after, int maxPages, CancellationToken ct);

    /// <summary>The numbers on the account.</summary>
    Task<List<ElksNumber>> GetNumbersAsync(ElksLogin login, CancellationToken ct);

    /// <summary>Sends an SMS. Returns 46elks' id for it.</summary>
    Task<string?> SendSmsAsync(ElksLogin login, string from, string to, string message, CancellationToken ct);
}

public sealed class ElksApi(HttpClient http) : IElksApi
{
    public const string BaseUrl = "https://api.46elks.com/a1/";

    public async Task<List<ElksSms>> GetIncomingAsync(ElksLogin login, string to, DateTime after, int maxPages, CancellationToken ct)
    {
        var found = new List<ElksSms>();
        string? before = null;

        // The history comes newest first; "next" points at the page before. Paging stops at the first SMS that is
        // not new, so normally one page is read. The time filters are not used: 46elks' docs describe start and end
        // the other way round from their names, and filtering here can't go wrong.
        for (var page = 0; page < maxPages; page++)
        {
            var url = $"sms?to={Uri.EscapeDataString(to)}&limit=100"
                      + (before is null ? "" : $"&start={Uri.EscapeDataString(before)}");
            var body = await GetAsync<HistoryPage>(login, url, ct) ?? new HistoryPage();

            var reachedOld = false;
            foreach (var row in body.Data ?? [])
            {
                if (row.Id is null || ParseCreated(row.Created) is not { } created) continue;
                if (created <= after) { reachedOld = true; continue; }
                if (row.Direction == "incoming") found.Add(new ElksSms(row.Id, created, row.From, row.To, row.Message));
            }

            if (reachedOld || body.Data is not { Count: > 0 } || string.IsNullOrEmpty(body.Next)) break;
            before = body.Next;
        }

        return found.DistinctBy(s => s.Id).OrderBy(s => s.Created).ToList();
    }

    public async Task<List<ElksNumber>> GetNumbersAsync(ElksLogin login, CancellationToken ct)
    {
        var body = await GetAsync<NumbersPage>(login, "numbers", ct);
        return (body?.Data ?? [])
            .Where(n => n.Number is not null)
            .Select(n => new ElksNumber(n.Number!, n.Active == "yes", n.Capabilities ?? [], n.SmsUrl))
            .ToList();
    }

    public async Task<string?> SendSmsAsync(ElksLogin login, string from, string to, string message, CancellationToken ct)
    {
        using var request = Request(login, HttpMethod.Post, "sms");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["from"] = from,
            ["to"] = to,
            ["message"] = message,
        });
        using var response = await SendAsync(request, ct);
        return (await response.Content.ReadFromJsonAsync<SentSms>(ct))?.Id;
    }

    /// <summary>46elks writes "2018-07-13T13:57:23.741000", in UTC without a zone.</summary>
    public static DateTime? ParseCreated(string? value)
        => DateTime.TryParse(value, CultureInfo.InvariantCulture,
               DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;

    private async Task<T?> GetAsync<T>(ElksLogin login, string url, CancellationToken ct)
    {
        using var request = Request(login, HttpMethod.Get, url);
        using var response = await SendAsync(request, ct);
        return await response.Content.ReadFromJsonAsync<T>(ct);
    }

    private static HttpRequestMessage Request(ElksLogin login, HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, BaseUrl + url);
        var auth = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{login.Username}:{login.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ElksException($"46elks gick inte att nå: {ex.Message}", transient: true);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ElksException("46elks svarade inte i tid.", transient: true);
        }

        if (response.IsSuccessStatusCode) return response;

        var detail = (await response.Content.ReadAsStringAsync(ct)).Trim();
        if (detail.Length > 200) detail = detail[..200];
        var status = response.StatusCode;
        response.Dispose();
        throw status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new ElksException("46elks godkände inte inloggningen. Kontrollera API-användarnamn och API-lösenord."),
            _ => new ElksException($"46elks svarade {(int)status}{(detail.Length > 0 ? $": {detail}" : ".")}",
                transient: (int)status >= 500),
        };
    }

    private sealed class HistoryPage
    {
        [JsonPropertyName("data")] public List<HistoryRow>? Data { get; set; }
        [JsonPropertyName("next")] public string? Next { get; set; }
    }

    private sealed class HistoryRow
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("created")] public string? Created { get; set; }
        [JsonPropertyName("direction")] public string? Direction { get; set; }
        [JsonPropertyName("from")] public string? From { get; set; }
        [JsonPropertyName("to")] public string? To { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
    }

    private sealed class NumbersPage
    {
        [JsonPropertyName("data")] public List<NumberRow>? Data { get; set; }
    }

    private sealed class NumberRow
    {
        [JsonPropertyName("number")] public string? Number { get; set; }
        [JsonPropertyName("active")] public string? Active { get; set; }
        [JsonPropertyName("capabilities")] public List<string>? Capabilities { get; set; }
        [JsonPropertyName("sms_url")] public string? SmsUrl { get; set; }
    }

    private sealed class SentSms
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
    }
}

/// <summary>
/// The 46elks login: the company's own, saved under SMS-larm (best a subaccount). There is no other: nothing about
/// 46elks is kept in appsettings or user-secrets.
/// </summary>
public sealed class ElksAccount(ISmsSettingsService settings)
{
    /// <summary>Null when none is saved. Throws <see cref="ElksException"/> when the saved one can't be read.</summary>
    public async Task<ElksLogin?> GetLoginAsync()
    {
        try
        {
            return await settings.GetElksLoginAsync();
        }
        catch (InvalidOperationException ex)
        {
            throw new ElksException(ex.Message);
        }
    }
}

/// <summary>Encrypts the saved 46elks password with ASP.NET Data Protection (keys kept by the server, DPAPI on Windows).</summary>
public sealed class DataProtectionSecretProtector(IDataProtectionProvider provider)
    : ISecretProtector
{
    private readonly IDataProtector _protector =
        provider.CreateProtector("Larmcentralen.SmsSettings.ElksPassword");

    public string Protect(string secret) => _protector.Protect(secret);

    public string? Unprotect(string protectedSecret)
    {
        try { return _protector.Unprotect(protectedSecret); }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
    }
}
