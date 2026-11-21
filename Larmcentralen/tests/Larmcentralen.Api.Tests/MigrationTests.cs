using Larmcentralen.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Larmcentralen.Api.Tests;

/// <summary>
/// Upgrading a database made before shared solutions: every (alarm, solution) pair and every id
/// must survive the move from Solutions.AlarmId to the AlarmSolutions link table.
/// </summary>
public sealed class MigrationTests : IDisposable
{
    private const string InitialCreate = "20261121094416_InitialCreate";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"larm-migration-{Guid.NewGuid():N}");
    private string DbPath => Path.Combine(_dir, "larmcentralen.db");

    public MigrationTests() => Directory.CreateDirectory(_dir);

    private AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite($"Data Source={DbPath}").Options);

    [Fact]
    public async Task Upgrade_from_InitialCreate_keeps_every_link_and_id()
    {
        await using (var db = CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(InitialCreate, Ct);
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO Areas (Id, Title, CreatedAt) VALUES (1, 'SQ-linjen', '2026-01-01');
                INSERT INTO Equipment (Id, Title, AreaId, CreatedAt) VALUES (10, 'T101', 1, '2026-01-01'), (11, 'T102', 1, '2026-01-01');
                INSERT INTO Alarms (Id, Title, AlarmCode, Severity, EquipmentId, CreatedAt) VALUES
                    (100, 'Temperatur för hög', 'M200.1', 'Hög', 10, '2026-01-01'),
                    (101, 'Temperatur för hög', 'M200.1', 'Hög', 11, '2026-01-01'),
                    (102, 'Utan lösning', NULL, 'Låg', 11, '2026-01-01');
                INSERT INTO Solutions (Id, Title, Content, SortOrder, AlarmId, CreatedAt) VALUES
                    (7, 'Byt termoelement', '<p>Steg 1: stäng av</p>', 10, 100, '2026-01-01'),
                    (8, 'Kontrollera regulatorn', NULL, 20, 100, '2026-01-01'),
                    (42, 'Återställ', '<ol><li>Öppna skåpet</li></ol>', 10, 101, '2026-01-01');
                """, Ct);
        }

        string? backup;
        await using (var db = CreateContext())
        {
            backup = await SqliteSetup.BackupBeforeMigratingAsync(db, DbPath);
            await db.Database.MigrateAsync(Ct);
        }

        await using var connection = new SqliteConnection($"Data Source={DbPath}");
        await connection.OpenAsync(Ct);

        Assert.Equal(
            [(100, 7), (100, 8), (101, 42)],
            await QueryAsync(connection, "SELECT AlarmId, SolutionId FROM AlarmSolutions ORDER BY AlarmId, SolutionId",
                r => (r.GetInt32(0), r.GetInt32(1))));
        Assert.Equal(
            [(7, "Byt termoelement", "<p>Steg 1: stäng av</p>"), (8, "Kontrollera regulatorn", null), (42, "Återställ", "<ol><li>Öppna skåpet</li></ol>")],
            await QueryAsync(connection, "SELECT Id, Title, Content FROM Solutions ORDER BY Id",
                r => (r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2))));
        Assert.Empty(await QueryAsync(connection,
            "SELECT name FROM sqlite_master WHERE name = '_SolutionAlarmStash'", r => r.GetString(0)));
        Assert.Empty(await QueryAsync(connection, "PRAGMA foreign_key_check", r => r.GetString(0)));
        Assert.Contains("EquipmentType", await QueryAsync(connection, "SELECT name FROM pragma_table_info('Equipment')", r => r.GetString(0)));
        Assert.DoesNotContain("AlarmId", await QueryAsync(connection, "SELECT name FROM pragma_table_info('Solutions')", r => r.GetString(0)));

        // A copy was written before migrating, and it still has the old shape.
        Assert.NotNull(backup);
        Assert.True(File.Exists(backup));
        await using var old = new SqliteConnection($"Data Source={backup};Mode=ReadOnly");
        await old.OpenAsync(Ct);
        Assert.Equal(3, (await QueryAsync(old, "SELECT AlarmId FROM Solutions", r => r.GetInt32(0))).Count);
    }

    [Fact]
    public async Task No_backup_for_a_new_database_or_one_already_up_to_date()
    {
        await using var db = CreateContext();
        Assert.Null(await SqliteSetup.BackupBeforeMigratingAsync(db, DbPath));
        await db.Database.MigrateAsync(Ct);
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
