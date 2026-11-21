using Larmcentralen.Client.Core.Models;
using Larmcentralen.Client.Core.Search;
using Larmcentralen.Client.Core.Storage;
using Microsoft.Data.Sqlite;

namespace Larmcentralen.Client.Core.Tests;

/// <summary>Shared solutions in the offline copy: the index, sibling suggestions and the v1 → v2 store upgrade.</summary>
public sealed class SharedSolutionTests : IDisposable
{
    private static readonly DateTime Created = new(2026, 3, 20, 9, 0, 0, DateTimeKind.Utc);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"larm-shared-{Guid.NewGuid():N}.db");

    /// <summary>Härdugnar T101/T102, an anlöpningsugn A03 and MAIN (Övrigt), all with an M200.0 alarm.</summary>
    private static SyncSnapshot Snapshot(bool withLinks = true) => new(
        Created,
        [new Area(1, "SQ-linjen (OCSS)", null, null)],
        [
            new Equipment(10, "T101", null, 1, null, "Härdugn"),
            new Equipment(11, "T102", null, 1, null, "Härdugn"),
            new Equipment(12, "A03", null, 1, null, "Anlöpningsugn"),
            new Equipment(13, "MAIN", null, 1, null, "Övrigt"),
            new Equipment(14, "W1", null, 1, null, null),
        ],
        [
            new Alarm(100, "Temperatur för hög", "M200.0", Severity.High, null, 10, Created, null),
            new Alarm(101, "Temperatur  för HÖG ", "M200.0", Severity.High, null, 11, Created, null),
            new Alarm(102, "Temperatur för hög", "M200.0", Severity.High, null, 12, Created, null),
            new Alarm(103, "Nödstopp", "M200.0", Severity.Critical, null, 13, Created, null),
            // Same text, other code, same type: suggested (text match).
            new Alarm(104, "Temperatur for hog", "M999.9", Severity.High, null, 11, Created, null),
            // Same code, other text, same type: suggested (code match), text shown so the difference is visible.
            new Alarm(105, "Gasflöde lågt", "M200.0", Severity.High, null, 11, Created, null),
            // No code, other text: never matched by a missing code.
            new Alarm(106, "Lampa trasig", null, Severity.Low, null, 10, Created, null),
            new Alarm(107, "Dörr öppen", null, Severity.Low, null, 11, Created, null),
            new Alarm(108, "Temperatur för hög", "M200.0", Severity.High, null, 14, Created, null),
        ],
        [
            new Solution(1000, "Delad lösning", "<p>Gemensam</p>", 10, null, 100, Created, null),
            new Solution(1001, "Egen lösning", null, 20, null, 101, Created, null),
        ],
        withLinks ? [new AlarmSolution(100, 1000), new AlarmSolution(101, 1000), new AlarmSolution(101, 1001)] : null);

    [Fact]
    public void A_shared_solution_shows_under_every_linked_alarm()
    {
        var index = new AlarmIndex(Snapshot());

        Assert.True(index.SupportsSharing);
        Assert.Equal([1000], index.GetDetails(100)!.Solutions.Select(s => s.Id));
        Assert.Equal([1000, 1001], index.GetDetails(101)!.Solutions.Select(s => s.Id));
        Assert.Equal(["T101", "T102"], index.AlarmsForSolution(1000).Select(a => a.Equipment.Title));
        Assert.Equal([101], index.AlarmsForSolution(1001).Select(a => a.Alarm.Id));
    }

    [Fact]
    public void Without_links_from_an_old_server_each_solution_uses_its_AlarmId_and_sharing_is_off()
    {
        var index = new AlarmIndex(Snapshot(withLinks: false));

        Assert.False(index.SupportsSharing);
        Assert.Equal([1000], index.GetDetails(100)!.Solutions.Select(s => s.Id));
        Assert.Equal([1001], index.GetDetails(101)!.Solutions.Select(s => s.Id));
        Assert.Equal([100], index.AlarmsForSolution(1000).Select(a => a.Alarm.Id));
    }

    [Fact]
    public void Siblings_are_alarms_of_the_same_unit_type_with_the_same_code_or_text()
    {
        var index = new AlarmIndex(Snapshot());

        // T101 M200.0 suggests T102's alarms only: never MAIN (Övrigt), A03 (Anlöpningsugn) or untyped W1.
        // Same text first (101, 104), then same code with other text (105).
        Assert.Equal([101, 104, 105], index.Siblings(100).Select(a => a.Alarm.Id));
    }

    [Fact]
    public void Units_without_a_type_or_of_type_Ovrigt_get_no_suggestions()
    {
        var index = new AlarmIndex(Snapshot());

        Assert.Empty(index.Siblings(103)); // MAIN, Övrigt
        Assert.Empty(index.Siblings(108)); // W1, no type
        Assert.Empty(index.Siblings(102)); // A03: the only anlöpningsugn
    }

    [Fact]
    public void A_missing_code_never_matches_by_code()
    {
        var index = new AlarmIndex(Snapshot());

        Assert.Empty(index.Siblings(106));
    }

    [Theory]
    [InlineData("Temperatur för hög", "temperatur  for HOG ")]
    [InlineData("Nivå\tlåg", "niva lag")]
    public void Text_normalising_ignores_case_spacing_and_accents(string a, string b)
        => Assert.Equal(AlarmIndex.NormaliseText(a), AlarmIndex.NormaliseText(b));

    [Fact]
    public void Units_are_grouped_by_type_with_Ovrigt_last()
    {
        var groups = new AlarmIndex(Snapshot()).EquipmentByType(null);

        Assert.Equal(["Anlöpningsugn", "Härdugn", "Övrigt"], groups.Select(g => g.Type));
        Assert.Equal(["MAIN", "W1"], groups[2].Units.Select(e => e.Title));
    }

    [Fact]
    public void Units_without_any_types_are_one_ungrouped_list()
    {
        var snapshot = Snapshot() with { Equipment = Snapshot().Equipment.Select(e => e with { EquipmentType = null }).ToList() };

        var groups = new AlarmIndex(snapshot).EquipmentByType(null);

        Assert.Single(groups);
        Assert.Null(groups[0].Type);
    }

    [Fact]
    public async Task Store_round_trips_links_and_types()
    {
        var store = new LocalStore(_dbPath);
        await store.InitializeAsync(Ct);
        await store.ReplaceAllAsync(Snapshot(), DateTimeOffset.UtcNow, Ct);

        var loaded = await store.LoadAllAsync(Ct);

        Assert.Equal(Snapshot().AlarmSolutions!.ToHashSet(), loaded.AlarmSolutions!.ToHashSet());
        Assert.Equal("Härdugn", loaded.Equipment.Single(e => e.Id == 10).EquipmentType);
        Assert.Equal([1000, 1001], new AlarmIndex(loaded).GetDetails(101)!.Solutions.Select(s => s.Id));
    }

    [Fact]
    public async Task Store_from_an_old_server_loads_without_links()
    {
        var store = new LocalStore(_dbPath);
        await store.InitializeAsync(Ct);
        await store.ReplaceAllAsync(Snapshot(withLinks: false), DateTimeOffset.UtcNow, Ct);

        var loaded = await store.LoadAllAsync(Ct);

        Assert.Null(loaded.AlarmSolutions);
        Assert.False(new AlarmIndex(loaded).SupportsSharing);
        Assert.Equal([1000], new AlarmIndex(loaded).GetDetails(100)!.Solutions.Select(s => s.Id));
    }

    [Fact]
    public async Task A_version_1_file_is_upgraded_in_place_and_still_works_offline()
    {
        // The local file exactly as version 1 of the app wrote it.
        await using (var c = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await c.OpenAsync(Ct);
            await using var cmd = c.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE Settings (Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
                CREATE TABLE Areas (Id INTEGER PRIMARY KEY, Title TEXT NOT NULL, DisplayName TEXT, UpdatedAt TEXT);
                CREATE TABLE Equipment (Id INTEGER PRIMARY KEY, Title TEXT NOT NULL, DisplayName TEXT, AreaId INTEGER NOT NULL, UpdatedAt TEXT);
                CREATE TABLE Alarms (Id INTEGER PRIMARY KEY, Title TEXT NOT NULL, AlarmCode TEXT, Severity TEXT NOT NULL,
                    Description TEXT, EquipmentId INTEGER NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT);
                CREATE TABLE Solutions (Id INTEGER PRIMARY KEY, Title TEXT NOT NULL, Content TEXT, SortOrder INTEGER NOT NULL,
                    EstimatedTime TEXT, AlarmId INTEGER NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT);
                CREATE INDEX IX_Solutions_AlarmId ON Solutions (AlarmId);
                INSERT INTO Settings VALUES ('SchemaVersion', '1'), ('LastSyncedAt', '2026-11-20T10:00:00.0000000+00:00');
                INSERT INTO Areas VALUES (1, 'OCSS', NULL, NULL);
                INSERT INTO Equipment VALUES (10, 'T101', 'Härdugn 1', 1, NULL);
                INSERT INTO Alarms VALUES (100, 'Temperatur för hög', 'M200.4', 'Hög', NULL, 10, '2026-03-20T09:00:00.0000000Z', NULL);
                INSERT INTO Solutions VALUES (1000, 'Byt termoelement', '<p>Stäng av ugnen.</p>', 10, '30 min', 100, '2026-03-20T09:00:00.0000000Z', NULL);
                """;
            await cmd.ExecuteNonQueryAsync(Ct);
        }

        var store = new LocalStore(_dbPath);
        await store.InitializeAsync(Ct);
        await store.InitializeAsync(Ct); // Safe to run twice.

        var loaded = await store.LoadAllAsync(Ct);
        var details = new AlarmIndex(loaded).GetDetails(100)!;
        Assert.Equal("<p>Stäng av ugnen.</p>", details.Solutions.Single().Content);
        Assert.Null(details.Equipment.EquipmentType);
        Assert.NotNull(await store.GetLastSyncedAtAsync(Ct));
        Assert.Equal("4", await store.GetSettingAsync("SchemaVersion", Ct));

        SqliteConnection.ClearAllPools();
        await using var check = new SqliteConnection($"Data Source={_dbPath}");
        await check.OpenAsync(Ct);
        await using var q = check.CreateCommand();
        q.CommandText = "SELECT AlarmId || ':' || SolutionId FROM AlarmSolutions";
        Assert.Equal("100:1000", await q.ExecuteScalarAsync(Ct) as string);

        // A later sync from a new server replaces everything, links and types included.
        await store.ReplaceAllAsync(Snapshot(), DateTimeOffset.UtcNow, Ct);
        Assert.Equal(3, (await store.LoadAllAsync(Ct)).AlarmSolutions!.Count);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            if (File.Exists(file)) File.Delete(file);
    }
}
