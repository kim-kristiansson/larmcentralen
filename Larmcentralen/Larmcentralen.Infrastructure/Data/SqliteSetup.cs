using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

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

        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    }
}
