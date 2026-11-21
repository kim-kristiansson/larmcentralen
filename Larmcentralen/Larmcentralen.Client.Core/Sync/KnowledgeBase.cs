using Larmcentralen.Client.Core.Api;
using Larmcentralen.Client.Core.Models;
using Larmcentralen.Client.Core.Search;
using Larmcentralen.Client.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Larmcentralen.Client.Core.Sync;

/// <summary>
/// The client's single source of truth. Reading and searching always use the local copy,
/// so they work offline. Sync pulls a full snapshot from the API whenever it can, and the
/// UI listens to <see cref="Changed"/> to refresh pages and the online/offline indicator.
/// </summary>
public sealed class KnowledgeBase(
    LocalStore store,
    LarmApiClient api,
    INetworkStatus network,
    ILogger<KnowledgeBase> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private CancellationTokenSource? _backgroundCts;
    private Task? _backgroundTask;
    private bool _initialized;

    /// <summary>Raised on the thread that made the change. UI components must marshal with InvokeAsync.</summary>
    public event EventHandler? Changed;

    public AlarmIndex Index { get; private set; } = AlarmIndex.Empty;
    public DateTimeOffset? LastSyncedAt { get; private set; }
    public bool IsSyncing { get; private set; }

    /// <summary>True when the device has a network and the last call to the API worked.</summary>
    public bool IsOnline => network.HasNetwork && _apiReachable;
    public string? LastSyncError { get; private set; }
    public bool HasLocalData => Index.AlarmCount > 0 || LastSyncedAt is not null;

    private bool _apiReachable = true;

    /// <summary>Opens the local database and loads it into memory. Call once at start-up, before any page reads.</summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;
        await store.InitializeAsync(ct);
        Index = new AlarmIndex(await store.LoadAllAsync(ct));
        LastSyncedAt = await store.GetLastSyncedAtAsync(ct);
        _initialized = true;
        network.Changed += OnNetworkChanged;
        RaiseChanged();
    }

    /// <summary>Pulls the full knowledge base from the API and replaces the local copy. Returns false when offline or on error.</summary>
    public async Task<bool> SyncAsync(CancellationToken ct = default)
    {
        if (!network.HasNetwork)
        {
            SetReachable(false, "Ingen nätverksanslutning.");
            return false;
        }

        await _syncLock.WaitAsync(ct);
        try
        {
            IsSyncing = true;
            RaiseChanged();

            var snapshot = await api.GetSnapshotAsync(ct);
            var syncedAt = DateTimeOffset.UtcNow;
            await store.ReplaceAllAsync(snapshot, syncedAt, ct);

            Index = new AlarmIndex(snapshot);
            LastSyncedAt = syncedAt;
            _apiReachable = true;
            LastSyncError = null;
            logger.LogInformation("Synced {Alarms} alarms and {Solutions} solutions", snapshot.Alarms.Count, snapshot.Solutions.Count);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Never swallow silently: log it and surface it in the status bar.
            logger.LogWarning(ex, "Sync failed");
            _apiReachable = false;
            LastSyncError = ex is HttpRequestException or TaskCanceledException
                ? "Servern går inte att nå."
                : "Synkningen misslyckades.";
            return false;
        }
        finally
        {
            IsSyncing = false;
            _syncLock.Release();
            RaiseChanged();
        }
    }

    /// <summary>Syncs now and then every <paramref name="interval"/> until disposed.</summary>
    public void StartBackgroundSync(TimeSpan interval)
    {
        if (_backgroundTask is not null) return;
        _backgroundCts = new CancellationTokenSource();
        var ct = _backgroundCts.Token;
        _backgroundTask = Task.Run(async () =>
        {
            await SyncAsync(ct);
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(ct))
                await SyncAsync(ct);
        }, ct);
    }

    /// <summary>
    /// Runs a write against the API. Editing needs a connection in v1, so this fails fast when
    /// offline, and re-syncs afterwards so the local copy shows the change straight away.
    /// </summary>
    public async Task<T> WriteAsync<T>(Func<LarmApiClient, CancellationToken, Task<T>> write, CancellationToken ct = default)
    {
        if (!network.HasNetwork) throw new OfflineException();
        T result;
        try
        {
            result = await write(api, ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Write to the API failed");
            SetReachable(false, "Servern går inte att nå.");
            throw new OfflineException(ex);
        }
        SetReachable(true, null);
        await SyncAsync(ct);
        return result;
    }

    public Task WriteAsync(Func<LarmApiClient, CancellationToken, Task> write, CancellationToken ct = default)
        => WriteAsync<bool>(async (client, token) => { await write(client, token); return true; }, ct);

    /// <summary>Calls the API without changing data (for example Word export). Needs a connection.</summary>
    public async Task<T> CallApiAsync<T>(Func<LarmApiClient, CancellationToken, Task<T>> call, CancellationToken ct = default)
    {
        if (!network.HasNetwork) throw new OfflineException();
        try
        {
            var result = await call(api, ct);
            SetReachable(true, null);
            return result;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Call to the API failed");
            SetReachable(false, "Servern går inte att nå.");
            throw new OfflineException(ex);
        }
    }

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        RaiseChanged();
        if (network.HasNetwork) _ = SyncAsync();
    }

    private void SetReachable(bool reachable, string? error)
    {
        if (_apiReachable == reachable && LastSyncError == error) return;
        _apiReachable = reachable;
        if (!reachable) LastSyncError = error;
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "A KnowledgeBase.Changed handler failed");
        }
    }

    public async ValueTask DisposeAsync()
    {
        network.Changed -= OnNetworkChanged;
        if (_backgroundCts is not null)
        {
            await _backgroundCts.CancelAsync();
            try { if (_backgroundTask is not null) await _backgroundTask; }
            catch (OperationCanceledException) { }
            _backgroundCts.Dispose();
        }
        _syncLock.Dispose();
    }
}

public class OfflineException(Exception? inner = null)
    : Exception("Det här kräver anslutning till servern.", inner);
