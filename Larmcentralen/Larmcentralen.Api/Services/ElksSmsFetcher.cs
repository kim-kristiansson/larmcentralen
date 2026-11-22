using Larmcentralen.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace Larmcentralen.Api.Services;

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

    /// <summary>After a failure, wait longer each time (up to this), so a wrong login doesn't hammer 46elks.</summary>
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = options.Value.FetchIntervalSeconds;
        if (seconds <= 0)
        {
            logger.LogInformation("Fetching SMS from 46elks is off (Email:FetchIntervalSeconds is 0).");
            return;
        }

        var interval = TimeSpan.FromSeconds(seconds);
        var wait = interval;
        while (!stoppingToken.IsCancellationRequested)
        {
            var ok = await FetchOnceAsync(stoppingToken) >= 0;
            wait = ok ? interval : TimeSpan.FromTicks(Math.Min(wait.Ticks * 2, MaxBackoff.Ticks));
            try { await Task.Delay(wait, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>One round: handles the SMS that came in since the last round. Returns how many, or -1 when it failed.</summary>
    public async Task<int> FetchOnceAsync(CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISmsSettingsService>();
        var now = DateTime.UtcNow;
        try
        {
            var state = await settings.GetFetchStateAsync();
            if (state.ElksNumber is null) return 0;
            if (await scope.ServiceProvider.GetRequiredService<ElksAccount>().GetLoginAsync() is not { } login) return 0;
            var api = scope.ServiceProvider.GetRequiredService<IElksApi>();

            if (state.FetchedUntil is not { } until)
            {
                var existing = await api.GetIncomingAsync(login, state.ElksNumber, DateTime.MinValue, 1, ct);
                await settings.RecordFetchAsync(now, existing.Count > 0 ? existing.Max(s => s.Created) : now, null);
                return 0;
            }

            var incoming = await api.GetIncomingAsync(login, state.ElksNumber, until, MaxPages, ct);
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
            var reason = ex is ElksException ? ex.Message : $"Kunde inte hämta SMS från 46elks: {ex.Message}";
            try { await settings.RecordFetchAsync(now, null, reason); }
            catch (Exception recordEx) { logger.LogError(recordEx, "Could not record the failed fetch"); }
            return -1;
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
