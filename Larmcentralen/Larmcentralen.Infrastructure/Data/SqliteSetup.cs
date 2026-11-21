using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Larmcentralen.Infrastructure.Data;

/// <summary>
/// Runs on every new SQLite connection. SQLite's own lower() and LIKE only fold A to Z,
/// so "nödstopp" would not match "NÖDSTOPP". Replacing lower() with .NET's Unicode-aware
/// version lets searches compare lower(column) against a lowercased pattern, å/ä/ö included.
/// </summary>
public sealed class SqliteConnectionInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        => Configure(connection);

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Configure(connection);
        return Task.CompletedTask;
    }

    private static void Configure(DbConnection connection)
    {
        if (connection is not SqliteConnection sqlite) return;

        sqlite.CreateFunction("lower", (string? value) => value?.ToLower(CultureInfo.InvariantCulture),
            isDeterministic: true);
    }
}

public static class SqliteSetup
{
    /// <summary>
    /// Creates the folder for the database file, applies migrations and turns on WAL,
    /// so reads keep working while an edit is being saved.
    /// </summary>
    public static async Task InitialiseAsync(AppDbContext db)
    {
        var connectionString = db.Database.GetConnectionString();
        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        var folder = Path.GetDirectoryName(Path.GetFullPath(dataSource));
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);

        await BackupBeforeMigratingAsync(db, dataSource);
        await MigrateAsync(db);
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    }

    /// <summary>
    /// Applies pending migrations. Alarm definitions need a step in C# between their two migrations:
    /// migrate up to AddAlarmDefinitions, group the alarms into definitions, then apply the rest.
    /// </summary>
    public static async Task MigrateAsync(AppDbContext db, CancellationToken ct = default)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        if (pending.Contains(Migrations._20261121172920_MoveAlarmTextToDefinitions.Id))
        {
            await db.GetService<IMigrator>().MigrateAsync(Migrations._20261121172910_AddAlarmDefinitions.Id, ct);
            await AlarmDefinitionBackfill.RunAsync(db, ct);
        }
        await db.Database.MigrateAsync(ct);
    }

    /// <summary>
    /// When an existing database is about to be migrated, writes a consistent copy next to it first
    /// (larmcentralen.before-migration-20261121-160000.db), so a migration that goes wrong never costs data.
    /// Returns the backup path, or null when nothing needed migrating (or the database is new).
    /// </summary>
    public static async Task<string?> BackupBeforeMigratingAsync(AppDbContext db, string dataSource)
    {
        if (string.IsNullOrEmpty(dataSource) || dataSource == ":memory:" || !File.Exists(dataSource)) return null;
        if (!(await db.Database.GetPendingMigrationsAsync()).Any()) return null;
        if (!(await db.Database.GetAppliedMigrationsAsync()).Any()) return null;

        var full = Path.GetFullPath(dataSource);
        var backup = Path.Combine(Path.GetDirectoryName(full)!,
            $"{Path.GetFileNameWithoutExtension(full)}.before-migration-{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(full)}");
        // VACUUM INTO takes a consistent snapshot, WAL contents included. It cannot take parameters.
        var sql = "VACUUM INTO '" + backup.Replace("'", "''") + "';";
        await db.Database.ExecuteSqlRawAsync(sql);
        return backup;
    }
}
