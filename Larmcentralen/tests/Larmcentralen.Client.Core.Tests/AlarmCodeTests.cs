using Larmcentralen.Client.Core.Models;
using Larmcentralen.Client.Core.Search;
using Larmcentralen.Client.Core.Storage;
using Microsoft.Data.Sqlite;

namespace Larmcentralen.Client.Core.Tests;

/// <summary>Reusable alarm codes in the offline copy: the code picker, related alarms and the store upgrade from version 3.</summary>
public sealed class AlarmCodeTests : IDisposable
{
    private static readonly DateTime Created = new(2026, 3, 20, 9, 0, 0, DateTimeKind.Utc);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"larm-codes-{Guid.NewGuid():N}.db");

    /// <summary>Four furnaces with M200.0 (own texts), A03 and MAIN with M200.0 too, and some M200.1/M10.0 alarms.</summary>
    private static SyncSnapshot Snapshot(bool withCodes = true) => new(
        Created,
        [new Area(1, "SQ-linjen (OCSS)", null, null)],
        [
            new Equipment(10, "T101", null, 1, null, "Härdugn"),
            new Equipment(11, "T102", null, 1, null, "Härdugn"),
            new Equipment(12, "T103", null, 1, null, "Härdugn"),
            new Equipment(13, "T104", null, 1, null, "Härdugn"),
            new Equipment(14, "A03", null, 1, null, "Anlöpningsugn"),
            new Equipment(20, "MAIN", null, 1, null, "Övrigt"),
            new Equipment(21, "N1", null, 1, null, null),
        ],
        [
            new Alarm(100, "Batteribortfall PLC", "M200.0", Severity.High, null, 10, Created, null, withCodes ? 1 : null),
            new Alarm(101, "Batteribortfall PLC", "M200.0", Severity.High, null, 11, Created, null, withCodes ? 1 : null),
            new Alarm(102, "PLC batteriavbrott", "M200.0", Severity.High, null, 12, Created, null, withCodes ? 1 : null),
            new Alarm(103, "PLC batteriavbrott", "M200.0", Severity.High, null, 13, Created, null, withCodes ? 1 : null),
            new Alarm(104, "Batteri", "M200.0", Severity.High, null, 14, Created, null, withCodes ? 1 : null),
            new Alarm(105, "Config Error", "M200.0", Severity.High, null, 20, Created, null, withCodes ? 1 : null),
            new Alarm(106, "Temperatur elskåp", "M200.1", Severity.High, null, 10, Created, null, withCodes ? 2 : null),
            new Alarm(107, "Nödstopp", "M10.0", Severity.Critical, null, 21, Created, null, withCodes ? 3 : null),
            new Alarm(108, "Lampa", null, Severity.Low, null, 10, Created, null),
        ],
        [new Solution(1000, "Byt batteri", "<p>Batteri</p>", 10, null, 100, Created, null)],
        [new AlarmSolution(100, 1000)],
        withCodes ? [new AlarmCode(1, "M200.0", null), new AlarmCode(2, "M200.1", null), new AlarmCode(3, "M10.0", "Nödstopp-krets"), new AlarmCode(4, "M999.9", null)] : null);

    [Fact]
    public void The_same_code_on_other_units_of_the_type_is_related_whatever_its_text()
    {
        var index = new AlarmIndex(Snapshot());

        Assert.Equal(["T102", "T103", "T104"], index.SameCode(100).Select(a => a.Equipment.Title));
        Assert.Equal(["T101", "T102", "T103"], index.SameCode(103).Select(a => a.Equipment.Title));
        Assert.Empty(index.SameCode(104)); // A03: no other anlöpningsugn has it
        Assert.Empty(index.SameCode(105)); // MAIN is Övrigt: never related across units
        Assert.Empty(index.SameCode(107)); // N1 has no type
        Assert.Empty(index.SameCode(108)); // no code
    }

    [Fact]
    public void Code_picker_lists_each_code_once_in_natural_order_with_how_many_units_use_it()
    {
        var index = new AlarmIndex(Snapshot());

        var all = index.SearchCodes(null, equipmentId: 14);
        Assert.Equal(["M10.0", "M200.0", "M200.1", "M999.9"], all.Select(o => o.Code.Code));
        Assert.Equal([1, 6, 1, 0], all.Select(o => o.UnitCount));
    }

    [Theory]
    [InlineData("200", new[] { "M200.0", "M200.1" })]
    [InlineData("m200.1", new[] { "M200.1" })]
    [InlineData(" 0.0 ", new[] { "M10.0", "M200.0" })]
    [InlineData("x", new string[0])]
    public void Code_picker_filters_by_any_part_of_the_code(string text, string[] expected)
        => Assert.Equal(expected, new AlarmIndex(Snapshot()).SearchCodes(text, equipmentId: 14).Select(o => o.Code.Code).Order());

    [Fact]
    public void Codes_the_unit_already_has_are_disabled_except_on_the_alarm_being_edited()
    {
        var index = new AlarmIndex(Snapshot());

        var options = index.SearchCodes("M200", equipmentId: 10);
        Assert.Equal("Finns redan på den här enheten", options.Single(o => o.Code.Code == "M200.0").DisabledReason);
        Assert.Equal("Finns redan på den här enheten", options.Single(o => o.Code.Code == "M200.1").DisabledReason);
        Assert.Null(index.SearchCodes("M200", equipmentId: 10, editingAlarmId: 100).Single(o => o.Code.Code == "M200.0").DisabledReason);
        Assert.Null(index.SearchCodes("M200", equipmentId: 14).Single(o => o.Code.Code == "M200.1").DisabledReason);
    }

    [Fact]
    public void Without_a_code_list_from_the_server_codes_are_still_related_by_their_text()
    {
        var index = new AlarmIndex(Snapshot(withCodes: false));

        Assert.False(index.SupportsAlarmCodes);
        Assert.Empty(index.SearchCodes(null, equipmentId: 10));
        Assert.Equal(["T102", "T103", "T104"], index.SameCode(100).Select(a => a.Equipment.Title));
    }

    [Fact]
    public async Task Store_round_trips_codes()
    {
        var store = new LocalStore(_dbPath);
        await store.InitializeAsync(Ct);
        await store.ReplaceAllAsync(Snapshot(), DateTimeOffset.UtcNow, Ct);

        var loaded = await store.LoadAllAsync(Ct);

        Assert.Equal(4, loaded.AlarmCodes!.Count);
        Assert.Equal("Nödstopp-krets", loaded.AlarmCodes.Single(c => c.Id == 3).Description);
        Assert.Equal(2, loaded.Alarms.Single(a => a.Id == 106).AlarmCodeId);
        Assert.True(new AlarmIndex(loaded).SupportsAlarmCodes);
    }

    [Fact]
    public async Task A_version_3_file_from_the_definitions_app_is_upgraded_in_place()
    {
        await using (var c = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await c.OpenAsync(Ct);
            await using var cmd = c.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE Settings (Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
                CREATE TABLE Areas (Id INTEGER PRIMARY KEY, Title TEXT NOT NULL, DisplayName TEXT, UpdatedAt TEXT);
                CREATE TABLE Equipment (Id INTEGER PRIMARY KEY, Title TEXT NOT NULL, DisplayName TEXT, AreaId INTEGER NOT NULL, UpdatedAt TEXT, EquipmentType TEXT);
                CREATE TABLE Alarms (Id INTEGER PRIMARY KEY, Title TEXT NOT NULL, AlarmCode TEXT, Severity TEXT NOT NULL,
                    Description TEXT, EquipmentId INTEGER NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT, AlarmDefinitionId INTEGER);
                CREATE TABLE Solutions (Id INTEGER PRIMARY KEY, Title TEXT NOT NULL, Content TEXT, SortOrder INTEGER NOT NULL,
                    EstimatedTime TEXT, AlarmId INTEGER NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT);
                CREATE TABLE AlarmSolutions (AlarmId INTEGER NOT NULL, SolutionId INTEGER NOT NULL, PRIMARY KEY (AlarmId, SolutionId));
                CREATE TABLE AlarmDefinitions (Id INTEGER PRIMARY KEY, EquipmentType TEXT NOT NULL, ScopeEquipmentId INTEGER NOT NULL,
                    AlarmCode TEXT, Title TEXT NOT NULL, Severity TEXT NOT NULL, Description TEXT, UpdatedAt TEXT);
                CREATE TABLE DefinitionSolutions (AlarmDefinitionId INTEGER NOT NULL, SolutionId INTEGER NOT NULL, PRIMARY KEY (AlarmDefinitionId, SolutionId));
                INSERT INTO Settings VALUES ('SchemaVersion', '3'), ('HasAlarmLinks', 'true'), ('HasDefinitions', 'true'),
                    ('LastSyncedAt', '2026-11-21T10:00:00.0000000+00:00');
                INSERT INTO Areas VALUES (1, 'OCSS', NULL, NULL);
                INSERT INTO Equipment VALUES (10, 'T101', NULL, 1, NULL, 'Härdugn');
                INSERT INTO Alarms VALUES (100, 'Temperatur elskåp', 'M200.1', 'Hög', NULL, 10, '2026-03-20T09:00:00.0000000Z', NULL, 1);
                INSERT INTO Solutions VALUES (1000, 'Test', '<p>T</p>', 10, NULL, 100, '2026-03-20T09:00:00.0000000Z', NULL);
                INSERT INTO AlarmSolutions VALUES (100, 1000);
                INSERT INTO AlarmDefinitions VALUES (1, 'Härdugn', 0, 'M200.1', 'Temperatur elskåp', 'Hög', NULL, NULL);
                INSERT INTO DefinitionSolutions VALUES (1, 1000);
                """;
            await cmd.ExecuteNonQueryAsync(Ct);
        }

        var store = new LocalStore(_dbPath);
        await store.InitializeAsync(Ct);
        await store.InitializeAsync(Ct); // Safe to run twice.
        Assert.Equal("4", await store.GetSettingAsync("SchemaVersion", Ct));
        Assert.Null(await store.GetSettingAsync("HasDefinitions", Ct));

        // Offline, before the next sync: alarms and their solutions are all there, the code list comes with the sync.
        var index = new AlarmIndex(await store.LoadAllAsync(Ct));
        Assert.Equal([1000], index.GetDetails(100)!.Solutions.Select(s => s.Id));
        Assert.False(index.SupportsAlarmCodes);

        SqliteConnection.ClearAllPools();
        await using var check = new SqliteConnection($"Data Source={_dbPath}");
        await check.OpenAsync(Ct);
        await using var q = check.CreateCommand();
        q.CommandText = "SELECT group_concat(name) FROM sqlite_master WHERE type = 'table' AND name LIKE '%Definition%'";
        Assert.Null(await q.ExecuteScalarAsync(Ct) as string);

        await store.ReplaceAllAsync(Snapshot(), DateTimeOffset.UtcNow, Ct);
        Assert.True(new AlarmIndex(await store.LoadAllAsync(Ct)).SupportsAlarmCodes);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            if (File.Exists(file)) File.Delete(file);
    }
}
