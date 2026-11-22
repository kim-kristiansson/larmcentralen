using Larmcentralen.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Larmcentralen.Api.Tests;

/// <summary>
/// Upgrading old databases through alarm definitions to reusable alarm codes: every alarm keeps its id,
/// gets its code and its own text, and no solution link is lost.
/// </summary>
public sealed class MigrationTests : IDisposable
{
    private const string InitialCreate = "20261121094416_InitialCreate";
    private const string AddAlarmSolutions = "20261121160201_AddAlarmSolutions";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"larm-migration-{Guid.NewGuid():N}");
    private string DbPath => Path.Combine(_dir, "larmcentralen.db");

    public MigrationTests() => Directory.CreateDirectory(_dir);

    private AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite($"Data Source={DbPath}").Options);

    /// <summary>A database as the first version left it, then given unit types and an extra link by the shared-solutions version.</summary>
    private async Task SeedOldDatabaseAsync()
    {
        await using var db = CreateContext();
        await db.GetService<IMigrator>().MigrateAsync(InitialCreate, Ct);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Areas (Id, Title, CreatedAt) VALUES (1, 'SQ-linjen', '2026-01-01 00:00:00');
            INSERT INTO Equipment (Id, Title, AreaId, CreatedAt) VALUES
                (10, 'T101', 1, '2026-01-01 00:00:00'), (11, 'T102', 1, '2026-01-01 00:00:00'),
                (12, 'MAIN', 1, '2026-01-01 00:00:00'), (13, 'N1', 1, '2026-01-01 00:00:00');
            INSERT INTO Alarms (Id, Title, AlarmCode, Severity, EquipmentId, CreatedAt) VALUES
                (100, 'Temperatur elskåp', 'M200.1', 'Hög', 10, '2026-01-02 00:00:00'),
                (101, 'TEMPERATUR  ELSKÅP', 'M200.1', 'Kritisk', 11, '2026-01-01 00:00:00'),
                (102, 'Kommunikation CP', 'M200.5', 'Medel', 11, '2026-01-01 00:00:00'),
                (103, 'Nödstopp', 'M200.0', 'Kritisk', 12, '2026-01-01 00:00:00'),
                (104, 'Nödstopp', 'M200.0', 'Kritisk', 13, '2026-01-01 00:00:00'),
                (105, 'Lampa trasig', NULL, 'Låg', 10, '2026-01-01 00:00:00');
            INSERT INTO Solutions (Id, Title, Content, SortOrder, AlarmId, CreatedAt) VALUES
                (7, 'Byt termoelement', '<p>Steg 1: stäng av</p>', 10, 100, '2026-01-01 00:00:00'),
                (8, 'Kontrollera fläkten', NULL, 20, 101, '2026-01-01 00:00:00'),
                (42, 'Starta om CP', '<ol><li>Öppna skåpet</li></ol>', 10, 102, '2026-01-01 00:00:00');
            """, Ct);

        await db.GetService<IMigrator>().MigrateAsync(AddAlarmSolutions, Ct);
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE Equipment SET EquipmentType = 'Härdugn' WHERE Id IN (10, 11);
            UPDATE Equipment SET EquipmentType = 'Övrigt' WHERE Id = 12;
            -- Shared before definitions existed: solution 7 on both furnaces.
            INSERT INTO AlarmSolutions (AlarmId, SolutionId) VALUES (101, 7);
            """, Ct);
    }

    [Fact]
    public async Task Upgrade_through_definitions_to_alarm_codes_keeps_every_alarm_id_and_link()
    {
        await SeedOldDatabaseAsync();

        string? backup;
        await using (var db = CreateContext())
        {
            backup = await SqliteSetup.BackupBeforeMigratingAsync(db, DbPath);
            await SqliteSetup.MigrateAsync(db, Ct);
            Assert.Empty(await db.Database.GetPendingMigrationsAsync(Ct));
        }

        await using var connection = new SqliteConnection($"Data Source={DbPath}");
        await connection.OpenAsync(Ct);

        var alarms = await QueryAsync(connection, """
            SELECT a.Id, a.Title, a.Severity, c.Code FROM Alarms a LEFT JOIN AlarmCodes c ON c.Id = a.AlarmCodeId ORDER BY a.Id
            """, r => (Id: r.GetInt32(0), Title: r.GetString(1), Severity: r.GetString(2), Code: r.IsDBNull(3) ? null : r.GetString(3)));
        Assert.Equal([100, 101, 102, 103, 104, 105], alarms.Select(a => a.Id));
        Assert.Equal(
            [
                (100, "Temperatur elskåp", "Kritisk", "M200.1"),
                // The shared definition's text and highest severity; the import tool's --restore-text brings back each unit's own spelling.
                (101, "Temperatur elskåp", "Kritisk", "M200.1"),
                (102, "Kommunikation CP", "Medel", "M200.5"),
                (103, "Nödstopp", "Kritisk", "M200.0"),
                (104, "Nödstopp", "Kritisk", "M200.0"),
                (105, "Lampa trasig", "Låg", null),
            ],
            alarms.Select(a => (a.Id, a.Title, a.Severity, a.Code)));

        // Each code once, whatever the unit or type.
        Assert.Equal(["M200.0", "M200.1", "M200.5"], await QueryAsync(connection, "SELECT Code FROM AlarmCodes ORDER BY Code", r => r.GetString(0)));

        // Every link from before definitions is back, plus the ones definitions shared (solution 8 now also on T101).
        var links = await QueryAsync(connection, "SELECT AlarmId, SolutionId FROM AlarmSolutions ORDER BY 1, 2", r => (r.GetInt32(0), r.GetInt32(1)));
        Assert.Equal([(100, 7), (100, 8), (101, 7), (101, 8), (102, 42)], links);
        Assert.Equal([7, 8, 42], await QueryAsync(connection, "SELECT Id FROM Solutions ORDER BY Id", r => r.GetInt32(0)));

        var tables = await QueryAsync(connection, "SELECT name FROM sqlite_master WHERE type = 'table'", r => r.GetString(0));
        foreach (var gone in new[] { "AlarmDefinitions", "DefinitionSolutions", "_AlarmSolutionStash", "_SolutionAlarmStash" })
            Assert.DoesNotContain(gone, tables);
        Assert.DoesNotContain("AlarmDefinitionId", await QueryAsync(connection, "SELECT name FROM pragma_table_info('Alarms')", r => r.GetString(0)));
        Assert.Empty(await QueryAsync(connection, "PRAGMA foreign_key_check", r => r.GetString(0)));

        // A copy was written before migrating, and it still has the old shape.
        Assert.NotNull(backup);
        await using var old = new SqliteConnection($"Data Source={backup};Mode=ReadOnly");
        await old.OpenAsync(Ct);
        Assert.Equal(4, (await QueryAsync(old, "SELECT AlarmId FROM AlarmSolutions", r => r.GetInt32(0))).Count);
    }

    [Fact]
    public async Task Running_the_upgrade_again_changes_nothing()
    {
        await SeedOldDatabaseAsync();
        await using var db = CreateContext();
        await SqliteSetup.MigrateAsync(db, Ct);

        await SqliteSetup.MigrateAsync(db, Ct);

        Assert.Equal(3, await db.AlarmCodes.CountAsync(Ct));
        Assert.Equal(5, await db.AlarmSolutions.CountAsync(Ct));
    }

    [Fact]
    public async Task No_backup_for_a_new_database_or_one_already_up_to_date()
    {
        await using var db = CreateContext();
        Assert.Null(await SqliteSetup.BackupBeforeMigratingAsync(db, DbPath));
        await SqliteSetup.MigrateAsync(db, Ct);
        Assert.Null(await SqliteSetup.BackupBeforeMigratingAsync(db, DbPath));
        Assert.Single(Directory.GetFiles(_dir, "*.db"));
    }

    private static async Task<List<T>> QueryAsync<T>(SqliteConnection connection, string sql, Func<SqliteDataReader, T> map)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var result = new List<T>();
        while (await reader.ReadAsync(Ct)) result.Add(map(reader));
        return result;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }
}
