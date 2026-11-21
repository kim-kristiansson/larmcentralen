using System.Net;
using System.Net.Http.Json;
using Larmcentralen.Client.Core.Api;
using Larmcentralen.Client.Core.Models;
using Larmcentralen.Client.Core.Search;
using Larmcentralen.Client.Core.Storage;
using Larmcentralen.Client.Core.Sync;
using Microsoft.Extensions.Logging.Abstractions;

namespace Larmcentralen.Client.Core.Tests;

public sealed class KnowledgeBaseTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"larm-test-{Guid.NewGuid():N}.db");
    private readonly FakeHandler _handler = new();
    private readonly FakeNetwork _network = new();

    private KnowledgeBase Create() => new(
        new LocalStore(_dbPath),
        new LarmApiClient(new HttpClient(_handler) { BaseAddress = new Uri("https://api.test/") }),
        _network,
        NullLogger<KnowledgeBase>.Instance);

    [Fact]
    public async Task Sync_stores_the_snapshot_and_search_works_after_restart_offline()
    {
        _handler.Respond = _ => JsonResponse(TestData.Snapshot());

        await using (var kb = Create())
        {
            await kb.InitializeAsync(TestContext.Current.CancellationToken);
            Assert.False(kb.HasLocalData);
            Assert.True(await kb.SyncAsync(TestContext.Current.CancellationToken));
            Assert.True(kb.IsOnline);
            Assert.NotNull(kb.LastSyncedAt);
        }

        // Restart with no network: everything comes from the local SQLite copy.
        _network.HasNetwork = false;
        _handler.Respond = _ => throw new HttpRequestException("no network");
        await using var offline = Create();
        await offline.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.False(offline.IsOnline);
        Assert.NotNull(offline.LastSyncedAt);
        Assert.Equal([103], offline.Index.Search(new AlarmSearchQuery("w2 temp")).Items.Select(a => a.Alarm.Id));

        var details = offline.Index.GetDetails(100)!;
        Assert.Equal("Temperatur för hög", details.Alarm.Title);
        // HTML and Swedish characters survive the round trip unchanged.
        Assert.Equal("<p>Steg 1: <strong>stäng av</strong> ugnen.</p>", details.Solutions[1].Content);
    }

    [Fact]
    public async Task Failed_sync_keeps_the_old_copy_and_reports_offline()
    {
        _handler.Respond = _ => JsonResponse(TestData.Snapshot());
        await using var kb = Create();
        await kb.InitializeAsync(TestContext.Current.CancellationToken);
        await kb.SyncAsync(TestContext.Current.CancellationToken);

        _handler.Respond = _ => throw new HttpRequestException("server down");
        Assert.False(await kb.SyncAsync(TestContext.Current.CancellationToken));

        Assert.False(kb.IsOnline);
        Assert.Equal("Servern går inte att nå.", kb.LastSyncError);
        Assert.Equal(6, kb.Index.AlarmCount);
    }

    [Fact]
    public async Task Sync_replaces_deleted_items()
    {
        _handler.Respond = _ => JsonResponse(TestData.Snapshot());
        await using var kb = Create();
        await kb.InitializeAsync(TestContext.Current.CancellationToken);
        await kb.SyncAsync(TestContext.Current.CancellationToken);

        var smaller = TestData.Snapshot() with { Alarms = TestData.Snapshot().Alarms.Where(a => a.Id != 100).ToList() };
        _handler.Respond = _ => JsonResponse(smaller);
        await kb.SyncAsync(TestContext.Current.CancellationToken);

        Assert.Null(kb.Index.GetDetails(100));
        var reloaded = new AlarmIndex(await new LocalStore(_dbPath).LoadAllAsync(TestContext.Current.CancellationToken));
        Assert.Null(reloaded.GetDetails(100));
    }

    [Fact]
    public async Task Writes_fail_fast_offline_and_resync_when_online()
    {
        _handler.Respond = _ => JsonResponse(TestData.Snapshot());
        await using var kb = Create();
        await kb.InitializeAsync(TestContext.Current.CancellationToken);

        _network.HasNetwork = false;
        await Assert.ThrowsAsync<OfflineException>(() =>
            kb.WriteAsync((api, ct) => api.DeleteAlarmAsync(100, ct), TestContext.Current.CancellationToken));
        Assert.Empty(_handler.Requests);

        _network.HasNetwork = true;
        _handler.Respond = req => req.Method == HttpMethod.Delete
            ? new HttpResponseMessage(HttpStatusCode.NoContent)
            : JsonResponse(TestData.Snapshot());
        await kb.WriteAsync((api, ct) => api.DeleteAlarmAsync(100, ct), TestContext.Current.CancellationToken);

        Assert.Equal(["DELETE api/alarms/100", "GET api/sync", "GET api/me"], _handler.Requests);
    }

    [Fact]
    public async Task Edit_access_comes_from_the_api_and_is_remembered_offline()
    {
        _handler.Respond = req => req.RequestUri!.AbsolutePath == "/api/me"
            ? JsonResponse(new AccessInfo("Admin", true))
            : JsonResponse(TestData.Snapshot());

        await using (var kb = Create())
        {
            await kb.InitializeAsync(TestContext.Current.CancellationToken);
            Assert.False(kb.CanEdit);
            await kb.SyncAsync(TestContext.Current.CancellationToken);
            Assert.True(kb.CanEdit);
        }

        _network.HasNetwork = false;
        await using var offline = Create();
        await offline.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.True(offline.CanEdit);
    }

    [Fact]
    public async Task Read_only_key_never_shows_edit()
    {
        _handler.Respond = req => req.RequestUri!.AbsolutePath == "/api/me"
            ? JsonResponse(new AccessInfo("User", false))
            : JsonResponse(TestData.Snapshot());
        await using var kb = Create();
        await kb.InitializeAsync(TestContext.Current.CancellationToken);
        await kb.SyncAsync(TestContext.Current.CancellationToken);

        Assert.False(kb.CanEdit);
    }

    [Fact]
    public async Task Forbidden_write_hides_edit_and_says_why()
    {
        _handler.Respond = req => req.RequestUri!.AbsolutePath == "/api/me"
            ? JsonResponse(new AccessInfo("Admin", true))
            : JsonResponse(TestData.Snapshot());
        await using var kb = Create();
        await kb.InitializeAsync(TestContext.Current.CancellationToken);
        await kb.SyncAsync(TestContext.Current.CancellationToken);
        Assert.True(kb.CanEdit);

        // The admin key was swapped for a read key since the last sync.
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.Forbidden);
        await Assert.ThrowsAsync<NotAllowedException>(() =>
            kb.WriteAsync((api, ct) => api.DeleteAlarmAsync(100, ct), TestContext.Current.CancellationToken));

        Assert.False(kb.CanEdit);
        Assert.True(kb.IsOnline);
    }

    [Fact]
    public async Task Api_errors_surface_with_status()
    {
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("missing") };
        await using var kb = Create();
        await kb.InitializeAsync(TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            kb.WriteAsync((api, ct) => api.UpdateAlarmAsync(5, new SaveAlarmRequest("x", null, "Låg", null, 10), ct),
                TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, ex.Status);
    }

    [Fact]
    public async Task Recent_alarms_are_newest_first_and_capped()
    {
        var store = new LocalStore(_dbPath);
        await store.InitializeAsync(TestContext.Current.CancellationToken);
        var recent = new RecentAlarms(store);

        for (var id = 1; id <= 12; id++) await recent.AddAsync(id, TestContext.Current.CancellationToken);
        await recent.AddAsync(5, TestContext.Current.CancellationToken);

        var ids = await recent.GetAsync(TestContext.Current.CancellationToken);
        Assert.Equal(RecentAlarms.MaxCount, ids.Count);
        Assert.Equal([5, 12, 11], ids.Take(3));
    }

    private static HttpResponseMessage JsonResponse<T>(T value)
        => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            if (File.Exists(file)) File.Delete(file);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add($"{request.Method} {request.RequestUri!.PathAndQuery.TrimStart('/')}");
            return Task.FromResult(Respond(request));
        }
    }

    private sealed class FakeNetwork : INetworkStatus
    {
        public bool HasNetwork { get; set; } = true;
        public event EventHandler? Changed { add { } remove { } }
    }
}
