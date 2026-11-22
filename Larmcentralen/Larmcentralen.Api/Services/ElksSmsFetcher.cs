using System.Globalization;
using System.Text.Json.Serialization;
using Larmcentralen.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace Larmcentralen.Api.Services;

/// <summary>An incoming SMS from 46elks' SMS history. <see cref="Created"/> is UTC.</summary>
public sealed record ElksSms(string Id, DateTime Created, string? From, string? To, string? Message);

/// <summary>46elks' SMS history (GET /a1/sms). Replaced in tests.</summary>
public interface IElksSmsHistory
{
    /// <summary>False until the 46elks account (Email:ElksUsername, Email:ElksPassword) is set.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Incoming SMS to <paramref name="to"/> created after <paramref name="after"/>, oldest first. Reads back through at
    /// most <paramref name="maxPages"/> pages of 100. Throws <see cref="ElksException"/> when 46elks says no.
    /// </summary>
    Task<List<ElksSms>> GetIncomingAsync(string to, DateTime after, int maxPages, CancellationToken ct);
}

public sealed class ElksException(string message) : Exception(message);

public sealed class ElksSmsHistory(HttpClient http, IOptions<EmailOptions> options) : IElksSmsHistory
{
    public const string ApiUrl = "https://api.46elks.com/a1/sms";

    public bool IsConfigured => options.Value.ElksUsername.Length > 0 && options.Value.ElksPassword.Length > 0;

    public async Task<List<ElksSms>> GetIncomingAsync(string to, DateTime after, int maxPages, CancellationToken ct)
    {
        var o = options.Value;
        var auth = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{o.ElksUsername}:{o.ElksPassword}"));
        var found = new List<ElksSms>();
        string? before = null;

        // The history comes newest first; "next" points at the page before it.
        for (var page = 0; page < maxPages; page++)
        {
            var url = $"{ApiUrl}?to={Uri.EscapeDataString(to)}&limit=100"
                      + (before is null ? "" : $"&start={Uri.EscapeDataString(before)}");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                throw new ElksException(response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? "46elks godkände inte inloggningen (Email:ElksUsername, Email:ElksPassword)."
                    : $"46elks svarade {(int)response.StatusCode} {response.ReasonPhrase}.");

            var body = await response.Content.ReadFromJsonAsync<HistoryPage>(ct) ?? new HistoryPage();
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

    /// <summary>46elks writes "2018-07-13T13:57:23.741000", in UTC without a zone.</summary>
    public static DateTime? ParseCreated(string? value)
        => DateTime.TryParse(value, CultureInfo.InvariantCulture,
               DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;

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
}

/// <summary>
/// Fetches new alarm SMS from 46elks' SMS history every <see cref="EmailOptions.FetchIntervalSeconds"/> seconds, so
/// the API needs no public address: it only calls out to 46elks. The 46elks number then needs no sms_url. The first
/// fetch only marks where the history ends, so old SMS are never acted on.
/// </summary>
public sealed class ElksSmsFetcher(
    IServiceScopeFactory scopes,
    IOptions<EmailOptions> options,
    ILogger<ElksSmsFetcher> logger) : BackgroundService
{
    /// <summary>Pages read back per fetch: 1000 SMS, far more than OCSS sends between two fetches.</summary>
    private const int MaxPages = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = options.Value.FetchIntervalSeconds;
        if (seconds <= 0)
        {
            logger.LogInformation("Fetching SMS from 46elks is off (Email:FetchIntervalSeconds is 0).");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await FetchOnceAsync(stoppingToken);
            try { await Task.Delay(TimeSpan.FromSeconds(seconds), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>One round: handles the SMS that came in since the last round. Returns how many.</summary>
    public async Task<int> FetchOnceAsync(CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var history = scope.ServiceProvider.GetRequiredService<IElksSmsHistory>();
        var settings = scope.ServiceProvider.GetRequiredService<ISmsSettingsService>();
        if (!history.IsConfigured) return 0;

        var state = await settings.GetFetchStateAsync();
        if (state.ElksNumber is null) return 0;

        var now = DateTime.UtcNow;
        try
        {
            if (state.FetchedUntil is not { } until)
            {
                var existing = await history.GetIncomingAsync(state.ElksNumber, DateTime.MinValue, 1, ct);
                await settings.RecordFetchAsync(now, existing.Count > 0 ? existing.Max(s => s.Created) : now, null);
                return 0;
            }

            var incoming = await history.GetIncomingAsync(state.ElksNumber, until, MaxPages, ct);
            var sms = scope.ServiceProvider.GetRequiredService<SmsAlarmService>();
            foreach (var message in incoming)
            {
                await sms.ProcessSmsAsync(message.From, message.To, message.Message, message.Id);
                // Mark each one as done, so a failure further on doesn't handle it twice.
                await settings.RecordFetchAsync(now, message.Created, null);
            }
            await settings.RecordFetchAsync(now, null, null);
            return incoming.Count;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Fetching SMS from 46elks failed");
            var reason = ex is ElksException ? ex.Message : $"Kunde inte nå 46elks: {ex.Message}";
            try { await settings.RecordFetchAsync(now, null, reason); }
            catch (Exception recordEx) { logger.LogError(recordEx, "Could not record the failed fetch"); }
            return 0;
        }
    }
}

/// <summary>
/// The ids of SMS handled lately, so an SMS that arrives both through the webhook and the history is handled once.
/// </summary>
public sealed class HandledSmsIds
{
    private const int Capacity = 1000;
    private readonly Lock _lock = new();
    private readonly HashSet<string> _ids = [];
    private readonly Queue<string> _order = new();

    /// <summary>True the first time an id is seen (and for SMS without an id).</summary>
    public bool TryMark(string? id)
    {
        if (string.IsNullOrEmpty(id)) return true;
        lock (_lock)
        {
            if (!_ids.Add(id)) return false;
            _order.Enqueue(id);
            if (_order.Count > Capacity) _ids.Remove(_order.Dequeue());
            return true;
        }
    }
}
