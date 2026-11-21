using Larmcentralen.Client.Core.Models;
using Larmcentralen.Client.Core.Search;
using Larmcentralen.Client.Core.Storage;
using Microsoft.Data.Sqlite;

namespace Larmcentralen.Client.Core.Tests;

/// <summary>Alarm definitions in the offline copy: solutions per definition, same-alarm units, targets and the v2 → v3 store upgrade.</summary>
public sealed class AlarmDefinitionTests : IDisposable
{
    private static readonly DateTime Created = new(2026, 3, 20, 9, 0, 0, DateTimeKind.Utc);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"larm-definitions-{Guid.NewGuid():N}.db");

    /// <summary>
    /// T101/T102 share M200.1 (definition 1). T103/T104 share M200.1 with another text (definition 2).
    /// T101 and MAIN both have M200.0 but MAIN is Övrigt, so definitions 3 and 4.
    /// </summary>
    private static SyncSnapshot Snapshot(bool withDefinitions = true) => new(
        Created,
        [new Area(1, "SQ-linjen (OCSS)", null, null)],
        [
            new Equipment(10, "T101", null, 1, null, "Härdugn"),
            new Equipment(11, "T102", null, 1, null, "Härdugn"),
            new Equipment(12, "T103", null, 1, null, "Härdugn"),
            new Equipment(13, "T104", null, 1, null, "Härdugn"),
            new Equipment(20, "MAIN", null, 1, null, "Övrigt"),
        ],
        [
            new Alarm(100, "Temperatur elskåp", "M200.1", Severity.High, null, 10, Created, null, withDefinitions ? 1 : null),
            new Alarm(101, "Temperatur elskåp", "M200.1", Severity.High, null, 11, Created, null, withDefinitions ? 1 : null),
            new Alarm(102, "ELSKÅPSTEMPERATUR", "M200.1", Severity.High, null, 12, Created, null, withDefinitions ? 2 : null),
            new Alarm(103, "ELSKÅPSTEMPERATUR", "M200.1", Severity.High, null, 13, Created, null, withDefinitions ? 2 : null),
            new Alarm(104, "Nödstopp", "M200.0", Severity.Critical, null, 10, Created, null, withDefinitions ? 3 : null),
            new Alarm(105, "Nödstopp", "M200.0", Severity.Critical, null, 20, Created, null, withDefinitions ? 4 : null),
        ],
        [new Solution(1000, "Kontrollera fläkten", "<p>Fläkt</p>", 10, null, 100, Created, null)],
        [new AlarmSolution(100, 1000), new AlarmSolution(101, 1000)],
        withDefinitions
            ?
            [
                new AlarmDefinition(1, "Härdugn", 0, "M200.1", "Temperatur elskåp", Severity.High, null, null),
                new AlarmDefinition(2, "Härdugn", 0, "M200.1", "ELSKÅPSTEMPERATUR", Severity.High, null, null),
                new AlarmDefinition(3, "Härdugn", 0, "M200.0", "Nödstopp", Severity.Critical, null, null),
                new AlarmDefinition(4, "Övrigt", 20, "M200.0", "Nödstopp", Severity.Critical, null, null),
            ]
            : null,
        withDefinitions ? [new DefinitionSolution(1, 1000)] : null);

    [Fact]
    public void A_solution_on_a_definition_shows_on_every_unit_that_shares_it()
    {
        var index = new AlarmIndex(Snapshot());

        Assert.True(index.SupportsDefinitions);
        Assert.Equal([1000], index.GetDetails(100)!.Solutions.Select(s => s.Id));
        Assert.Equal([1000], index.GetDetails(101)!.Solutions.Select(s => s.Id));
        Assert.Empty(index.GetDetails(102)!.Solutions);
        Assert.Equal(["T102"], index.SameDefinition(100).Select(a => a.Equipment.Title));
        Assert.Equal(["T101", "T102"], index.AlarmsForSolution(1000).Select(a => a.Equipment.Title));

        var target = Assert.Single(index.TargetsForSolution(1000));
        Assert.Equal(1, target.DefinitionId);
        Assert.Equal(["T101", "T102"], target.Units);
    }

    [Fact]
    public void Ovrigt_units_are_separate_definitions_and_get_no_suggestions()
    {
        var index = new AlarmIndex(Snapshot());

        Assert.NotEqual(index.TargetFor(104)!.Key, index.TargetFor(105)!.Key);
        Assert.Empty(index.SameDefinition(105));
        Assert.Empty(index.SiblingTargets(105));
    }

    [Fact]
    public void Sibling_targets_are_other_definitions_of_the_type_one_row_each()
    {
        var index = new AlarmIndex(Snapshot());

        // T101 M200.1: T102 is already the same definition; T103 and T104 are one other definition (same code).
        var sibling = Assert.Single(index.SiblingTargets(100));
        Assert.Equal(2, sibling.DefinitionId);
        Assert.Equal(["T103", "T104"], sibling.Units);
    }

    [Fact]
    public void Without_definitions_everything_works_per_alarm()
    {
        var index = new AlarmIndex(Snapshot(withDefinitions: false));

        Assert.False(index.SupportsDefinitions);
        Assert.True(index.SupportsSharing);
        Assert.Empty(index.SameDefinition(100));
        Assert.Equal([1000], index.GetDetails(101)!.Solutions.Select(s => s.Id));
        Assert.Equal([-101, -100], index.TargetsForSolution(1000).Select(t => t.Key).Order());
        Assert.Equal(-100, index.TargetFor(100)!.Key);
    }

    [Fact]
    public async Task Store_round_trips_definitions_and_their_links()
    {
        var store = new LocalStore(_dbPath);
        await store.InitializeAsync(Ct);
        await store.ReplaceAllAsync(Snapshot(), DateTimeOffset.UtcNow, Ct);

        var loaded = await store.LoadAllAsync(Ct);

        Assert.Equal(4, loaded.AlarmDefinitions!.Count);
        Assert.Equal([new DefinitionSolution(1, 1000)], loaded.DefinitionSolutions!);
        Assert.Equal(1, loaded.Alarms.Single(a => a.Id == 101).AlarmDefinitionId);
        Assert.Equal(["T102"], new AlarmIndex(loaded).SameDefinition(100).Select(a => a.Equipment.Title));
    }

    [Fact]
    public async Task A_version_2_file_is_upgraded_in_place_and_works_until_the_next_sync()
    {
        // As the shared-solutions version of the app left it.
        await using (var c = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await c.OpenAsync(Ct);
            await using var cmd = c.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE Settings (Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
                CREATE TABLE Areas (Id INTEGER PRIMARY KEY, Title TEXT NOT NULL, DisplayName TEXT, UpdatedAt TEXT);
                CREATE TABLE Equipment (Id INTEGER PRIMARY KEY, Title TEXT NOT NULL, DisplayName TEXT, AreaId INTEGER NOT NULL, UpdatedAt TEXT, EquipmentType TEXT);
                CREATE TABLE Alarms (Id INTEGER PRIMARY KEY, Title TEXT NOT NULL, AlarmCode TEXT, Severity TEXT NOT NULL,
                    Description TEXT, EquipmentId INTEGER NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT);
                CREATE TABLE Solutions (Id INTEGER PRIMARY KEY, Title TEXT NOT NULL, Content TEXT, SortOrder INTEGER NOT NULL,
                    EstimatedTime TEXT, AlarmId INTEGER NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT);
                CREATE TABLE AlarmSolutions (AlarmId INTEGER NOT NULL, SolutionId INTEGER NOT NULL, PRIMARY KEY (AlarmId, SolutionId));
                INSERT INTO Settings VALUES ('SchemaVersion', '2'), ('HasAlarmLinks', 'true'), ('LastSyncedAt', '2026-11-20T10:00:00.0000000+00:00');
                INSERT INTO Areas VALUES (1, 'OCSS', NULL, NULL);
                INSERT INTO Equipment VALUES (10, 'T101', NULL, 1, NULL, 'Härdugn'), (11, 'T102', NULL, 1, NULL, 'Härdugn');
                INSERT INTO Alarms VALUES (100, 'Temperatur elskåp', 'M200.1', 'Hög', NULL, 10, '2026-03-20T09:00:00.0000000Z', NULL),
                                          (101, 'Temperatur elskåp', 'M200.1', 'Hög', NULL, 11, '2026-03-20T09:00:00.0000000Z', NULL);
                INSERT INTO Solutions VALUES (1000, 'Kontrollera fläkten', '<p>Fläkt</p>', 10, NULL, 100, '2026-03-20T09:00:00.0000000Z', NULL);
                INSERT INTO AlarmSolutions VALUES (100, 1000), (101, 1000);
                """;
            await cmd.ExecuteNonQueryAsync(Ct);
        }

        var store = new LocalStore(_dbPath);
        await store.InitializeAsync(Ct);
        await store.InitializeAsync(Ct); // Safe to run twice.
        Assert.Equal("3", await store.GetSettingAsync("SchemaVersion", Ct));

        // Offline, before any sync: per-alarm links as before, no definitions.
        var offline = new AlarmIndex(await store.LoadAllAsync(Ct));
        Assert.False(offline.SupportsDefinitions);
        Assert.Equal([1000], offline.GetDetails(101)!.Solutions.Select(s => s.Id));

        // The next sync from a server with definitions fills them in.
        await store.ReplaceAllAsync(Snapshot(), DateTimeOffset.UtcNow, Ct);
        Assert.True(new AlarmIndex(await store.LoadAllAsync(Ct)).SupportsDefinitions);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            if (File.Exists(file)) File.Delete(file);
    }
}
