using Larmcentralen.Application.Interfaces;

namespace Larmcentralen.Api.Services;

/// <summary>
/// Erases removals that have been in the bin (Papperskorg) longer than the retention period: at start-up and then
/// every hour. A failure is logged and tried again next round.
/// </summary>
public sealed class TrashEraseService(IServiceScopeFactory scopes, ILogger<TrashEraseService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var erased = await scope.ServiceProvider.GetRequiredService<ITrashService>().EraseExpiredAsync(DateTime.UtcNow);
                if (erased > 0) logger.LogInformation("Erased {Count} removals from the bin", erased);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Erasing old removals from the bin failed");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
