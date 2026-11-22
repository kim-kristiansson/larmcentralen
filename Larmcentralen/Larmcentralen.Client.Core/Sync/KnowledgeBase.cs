using System.Net;
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
    private Task? _healthTask;
    private bool _initialized;

    /// <summary>Raised on the thread that made the change. UI components must marshal with InvokeAsync.</summary>
    public event EventHandler? Changed;

    public AlarmIndex Index { get; private set; } = AlarmIndex.Empty;
    public DateTimeOffset? LastSyncedAt { get; private set; }
    public bool IsSyncing { get; private set; }

    /// <summary>True when the device has a network and the server answered the last call or health check.</summary>
    public bool IsOnline => network.HasNetwork && _apiReachable;

    /// <summary>What the status bar shows: connected, the server cannot be reached, or no network at all.</summary>
    public ConnectionState Connection =>
        !network.HasNetwork ? ConnectionState.NoNetwork
        : _apiReachable ? ConnectionState.Online
        : ConnectionState.ServerUnreachable;

    /// <summary>Shown when a call to the server fails because it does not answer.</summary>
    public const string ServerUnreachableError = "Servern går inte att nå.";

    /// <summary>How often <see cref="StartBackgroundSync"/> checks that the server answers, and how long a check may take.</summary>
    public static readonly TimeSpan DefaultHealthCheckInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromSeconds(5);
    public string? LastSyncError { get; private set; }
    public bool HasLocalData => Index.AlarmCount > 0 || LastSyncedAt is not null;

    /// <summary>
    /// True when this app's API key is an admin key. Learned from the API on every sync and saved
    /// locally, so the UI knows offline too. False until the API has said otherwise.
    /// The API enforces it; this only decides which buttons to show.
    /// </summary>
    public bool CanEdit { get; private set; }

    private const string CanEditSetting = "CanEdit";

    /// <summary>Shown when the API answers 401/403 to a sync: reachable, but this PC's key is missing or wrong.</summary>
    public const string MissingKeyError = "API-nyckeln saknas eller är fel. Be en administratör om en nyckel.";

    private bool _apiReachable = true;

    /// <summary>Opens the local database and loads it into memory. Call once at start-up, before any page reads.</summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;
        await store.InitializeAsync(ct);
        Index = new AlarmIndex(await store.LoadAllAsync(ct));
        LastSyncedAt = await store.GetLastSyncedAtAsync(ct);
        CanEdit = await store.GetSettingAsync(CanEditSetting, ct) == "true";
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
            var access = await api.GetAccessAsync(ct);
            var syncedAt = DateTimeOffset.UtcNow;
            await store.ReplaceAllAsync(snapshot, syncedAt, ct);
            await SetCanEditAsync(access.CanEdit, ct);

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
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            // The server answered, so we are online: the key is missing or wrong. Say so instead of "offline".
            logger.LogWarning(ex, "Sync refused by the API ({Status}): API key missing or invalid", ex.StatusCode);
            _apiReachable = true;
            LastSyncError = MissingKeyError;
            return false;
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

    /// <summary>How soon to try again after a failed sync, so the app comes back online soon after the server does.</summary>
    public static readonly TimeSpan DefaultRetryInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Syncs now and then every <paramref name="interval"/> until disposed. After a failed sync it
    /// tries again after <paramref name="retryInterval"/> (default 30 s) instead of waiting a full interval,
    /// so an API that starts after the app, or restarts, is picked up quickly.
    /// Alongside, every <paramref name="healthCheckInterval"/> (default 30 s) it checks that the server still answers
    /// (<see cref="CheckServerAsync"/>), so the status bar shows a server that went down within about that time,
    /// not only at the next sync.
    /// </summary>
    public void StartBackgroundSync(TimeSpan interval, TimeSpan? retryInterval = null, TimeSpan? healthCheckInterval = null)
    {
        if (_backgroundTask is not null) return;
        var retry = retryInterval ?? DefaultRetryInterval;
        if (retry > interval) retry = interval;
        var health = healthCheckInterval ?? DefaultHealthCheckInterval;
        _backgroundCts = new CancellationTokenSource();
        var ct = _backgroundCts.Token;
        _backgroundTask = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                var ok = await SyncAsync(ct);
                await Task.Delay(ok ? interval : retry, ct);
            }
        }, ct);
        // One check at a time: the next waits for the previous to finish.
        _healthTask = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(health, ct);
                await CheckServerAsync(ct);
            }
        }, ct);
    }

    /// <summary>
    /// Checks that the server answers (a cheap GET with a short timeout) and updates <see cref="Connection"/>.
    /// No answer, a timeout, or 502/503/504 from a proxy means unreachable. Any other answer, even 401/403, means
    /// the server is there. When it comes back after being unreachable, the local copy is synced straight away.
    /// Returns true when the server answered.
    /// </summary>
    public async Task<bool> CheckServerAsync(CancellationToken ct = default)
    {
        if (!network.HasNetwork)
        {
            RaiseChanged();
            return false;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(HealthCheckTimeout);
        try
        {
            var status = await api.PingAsync(timeout.Token);
            if (IsProxyError(status))
            {
                MarkUnreachable($"Health check: the server answered {(int)status}");
                return false;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            MarkUnreachable($"Health check failed: {ex.Message}");
            return false;
        }

        if (_apiReachable) return true;
        logger.LogInformation("The server answers again; syncing");
        SetReachable(true, null);
        await SyncAsync(ct);
        return true;
    }

    /// <summary>502/503/504: a proxy or gateway answered because the server behind it did not.</summary>
    private static bool IsProxyError(HttpStatusCode status)
        => status is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    private void MarkUnreachable(string reason)
    {
        if (_apiReachable) logger.LogWarning("The server cannot be reached: {Reason}", reason);
        SetReachable(false, ServerUnreachableError);
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
        catch (Exception ex) when (IsUnreachable(ex, ct))
        {
            logger.LogWarning(ex, "Write to the API failed");
            SetReachable(false, ServerUnreachableError);
            throw new OfflineException(ex);
        }
        catch (ApiException ex) when (ex.Status == System.Net.HttpStatusCode.Forbidden)
        {
            // The key lost its admin rights since the last sync: hide the edit buttons straight away.
            logger.LogWarning("Write refused: this API key may only read");
            SetReachable(true, null);
            await SetCanEditAsync(false, ct);
            throw new NotAllowedException(ex);
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
        catch (Exception ex) when (IsUnreachable(ex, ct))
        {
            logger.LogWarning(ex, "Call to the API failed");
            SetReachable(false, ServerUnreachableError);
            throw new OfflineException(ex);
        }
    }

    /// <summary>
    /// True for failures that mean the server did not answer: no connection, a timeout (not a cancel the caller asked
    /// for), or 502/503/504 from a proxy in front of it.
    /// </summary>
    private static bool IsUnreachable(Exception ex, CancellationToken ct) => ex switch
    {
        HttpRequestException { StatusCode: { } status } => IsProxyError(status),
        HttpRequestException => true,
        OperationCanceledException => !ct.IsCancellationRequested,
        ApiException api => IsProxyError(api.Status),
        _ => false
    };

    private async Task SetCanEditAsync(bool canEdit, CancellationToken ct)
    {
        if (CanEdit == canEdit) return;
        CanEdit = canEdit;
        await store.SetSettingAsync(CanEditSetting, canEdit ? "true" : "false", ct);
        RaiseChanged();
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
            try { if (_healthTask is not null) await _healthTask; }
            catch (OperationCanceledException) { }
            _backgroundCts.Dispose();
        }
        _syncLock.Dispose();
    }
}

public class OfflineException(Exception? inner = null)
    : Exception("Det här kräver anslutning till servern.", inner);

/// <summary>The API refused a change because this app's key may only read.</summary>
public class NotAllowedException(Exception? inner = null)
    : Exception("Du har bara läsbehörighet. Be en administratör om ändringen.", inner);

/// <summary>What the app knows about its connection to the server.</summary>
public enum ConnectionState
{
    /// <summary>The server answered the last call or check.</summary>
    Online,
    /// <summary>The device has a network, but the server does not answer.</summary>
    ServerUnreachable,
    /// <summary>The device has no network at all.</summary>
    NoNetwork
}
