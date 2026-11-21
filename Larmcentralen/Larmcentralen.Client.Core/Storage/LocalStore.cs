using System.Globalization;
using Larmcentralen.Client.Core.Models;
using Microsoft.Data.Sqlite;

namespace Larmcentralen.Client.Core.Storage;

/// <summary>
/// The client's offline copy: areas, equipment, alarms and solutions in a local SQLite file,
/// plus small key/value settings (last sync time, recently viewed alarms).
/// Text is stored as SQLite TEXT, which is always UTF-8, so Swedish characters and HTML
/// round-trip unchanged.
/// </summary>
public class LocalStore(string databasePath)
{
    // 1: first version. 2: AlarmSolutions link table (shared solutions) and Equipment.EquipmentType.
    private const int SchemaVersion = 2;
    private const string HasAlarmLinksSetting = "HasAlarmLinks";
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate
    }.ToString();

    public string DatabasePath { get; } = databasePath;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(DatabasePath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        await using var connection = await OpenAsync(ct);
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(ct);

        // Read the version this file was written with before anything overwrites it.
        await ExecuteAsync(connection, tx, "CREATE TABLE IF NOT EXISTS Settings (Key TEXT PRIMARY KEY, Value TEXT NOT NULL);", ct);
        var stored = int.TryParse(await ScalarAsync(connection, tx, "SELECT Value FROM Settings WHERE Key = 'SchemaVersion'", ct),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

        await ExecuteAsync(connection, tx, """
            CREATE TABLE IF NOT EXISTS Areas (
                Id INTEGER PRIMARY KEY, Title TEXT NOT NULL, DisplayName TEXT, UpdatedAt TEXT);
            CREATE TABLE IF NOT EXISTS Equipment (
                Id INTEGER PRIMARY KEY, Title TEXT NOT NULL, DisplayName TEXT, AreaId INTEGER NOT NULL, UpdatedAt TEXT,
                EquipmentType TEXT);
            CREATE TABLE IF NOT EXISTS Alarms (
                Id INTEGER PRIMARY KEY, Title TEXT NOT NULL, AlarmCode TEXT, Severity TEXT NOT NULL,
                Description TEXT, EquipmentId INTEGER NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT);
            CREATE TABLE IF NOT EXISTS Solutions (
                Id INTEGER PRIMARY KEY, Title TEXT NOT NULL, Content TEXT, SortOrder INTEGER NOT NULL,
                EstimatedTime TEXT, AlarmId INTEGER NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT);
            CREATE INDEX IF NOT EXISTS IX_Solutions_AlarmId ON Solutions (AlarmId);
            CREATE TABLE IF NOT EXISTS AlarmSolutions (
                AlarmId INTEGER NOT NULL, SolutionId INTEGER NOT NULL, PRIMARY KEY (AlarmId, SolutionId));
            CREATE INDEX IF NOT EXISTS IX_AlarmSolutions_SolutionId ON AlarmSolutions (SolutionId);
            """, ct);

        // Version 1 files: Equipment has no EquipmentType yet, and each solution's alarm is only in Solutions.AlarmId.
        var equipmentColumns = await QueryAsync(connection, tx, "SELECT name FROM pragma_table_info('Equipment')", r => r.GetString(0), ct);
        if (!equipmentColumns.Contains("EquipmentType", StringComparer.OrdinalIgnoreCase))
            await ExecuteAsync(connection, tx, "ALTER TABLE Equipment ADD COLUMN EquipmentType TEXT;", ct);
        if (stored < 2)
            await ExecuteAsync(connection, tx, "INSERT OR IGNORE INTO AlarmSolutions (AlarmId, SolutionId) SELECT AlarmId, Id FROM Solutions;", ct);

        // Never lower the version: a newer app may have written this file.
        await SetSettingAsync(connection, tx, "SchemaVersion",
            Math.Max(stored, SchemaVersion).ToString(CultureInfo.InvariantCulture), ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>Replaces the whole local copy with a snapshot from the API, in one transaction.</summary>
    public async Task ReplaceAllAsync(SyncSnapshot snapshot, DateTimeOffset syncedAt, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(ct);

        await ExecuteAsync(connection, tx, "DELETE FROM AlarmSolutions; DELETE FROM Solutions; DELETE FROM Alarms; DELETE FROM Equipment; DELETE FROM Areas;", ct);

        foreach (var a in snapshot.Areas)
            await ExecuteAsync(connection, tx,
                "INSERT INTO Areas (Id, Title, DisplayName, UpdatedAt) VALUES ($id, $title, $display, $updated)", ct,
                ("$id", a.Id), ("$title", a.Title), ("$display", a.DisplayName), ("$updated", ToText(a.UpdatedAt)));

        foreach (var e in snapshot.Equipment)
            await ExecuteAsync(connection, tx,
                "INSERT INTO Equipment (Id, Title, DisplayName, AreaId, UpdatedAt, EquipmentType) VALUES ($id, $title, $display, $area, $updated, $type)", ct,
                ("$id", e.Id), ("$title", e.Title), ("$display", e.DisplayName), ("$area", e.AreaId), ("$updated", ToText(e.UpdatedAt)),
                ("$type", e.EquipmentType));

        foreach (var a in snapshot.Alarms)
            await ExecuteAsync(connection, tx,
                """
                INSERT INTO Alarms (Id, Title, AlarmCode, Severity, Description, EquipmentId, CreatedAt, UpdatedAt)
                VALUES ($id, $title, $code, $severity, $description, $equipment, $created, $updated)
                """, ct,
                ("$id", a.Id), ("$title", a.Title), ("$code", a.AlarmCode), ("$severity", a.Severity),
                ("$description", a.Description), ("$equipment", a.EquipmentId),
                ("$created", ToText(a.CreatedAt)), ("$updated", ToText(a.UpdatedAt)));

        foreach (var s in snapshot.Solutions)
            await ExecuteAsync(connection, tx,
                """
                INSERT INTO Solutions (Id, Title, Content, SortOrder, EstimatedTime, AlarmId, CreatedAt, UpdatedAt)
                VALUES ($id, $title, $content, $sort, $time, $alarm, $created, $updated)
                """, ct,
                ("$id", s.Id), ("$title", s.Title), ("$content", s.Content), ("$sort", s.SortOrder),
                ("$time", s.EstimatedTime), ("$alarm", s.AlarmId),
                ("$created", ToText(s.CreatedAt)), ("$updated", ToText(s.UpdatedAt)));

        // A server that predates shared solutions sends no links: each solution then belongs to its AlarmId.
        var links = snapshot.AlarmSolutions ?? snapshot.Solutions.Select(s => new AlarmSolution(s.AlarmId, s.Id)).ToList();
        foreach (var l in links)
            await ExecuteAsync(connection, tx,
                "INSERT OR IGNORE INTO AlarmSolutions (AlarmId, SolutionId) VALUES ($alarm, $solution)", ct,
                ("$alarm", l.AlarmId), ("$solution", l.SolutionId));
        await SetSettingAsync(connection, tx, HasAlarmLinksSetting, snapshot.AlarmSolutions is null ? "false" : "true", ct);

        await SetSettingAsync(connection, tx, "LastSyncedAt", syncedAt.ToString("O", CultureInfo.InvariantCulture), ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>Reads the whole local copy. Used at start-up to build the in-memory search index.</summary>
    public async Task<SyncSnapshot> LoadAllAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);

        var areas = await QueryAsync(connection, "SELECT Id, Title, DisplayName, UpdatedAt FROM Areas", r =>
            new Area(r.GetInt32(0), r.GetString(1), NullableString(r, 2), NullableDate(r, 3)), ct);

        var equipment = await QueryAsync(connection, "SELECT Id, Title, DisplayName, AreaId, UpdatedAt, EquipmentType FROM Equipment", r =>
            new Equipment(r.GetInt32(0), r.GetString(1), NullableString(r, 2), r.GetInt32(3), NullableDate(r, 4), NullableString(r, 5)), ct);

        var alarms = await QueryAsync(connection,
            "SELECT Id, Title, AlarmCode, Severity, Description, EquipmentId, CreatedAt, UpdatedAt FROM Alarms", r =>
            new Alarm(r.GetInt32(0), r.GetString(1), NullableString(r, 2), r.GetString(3), NullableString(r, 4),
                r.GetInt32(5), ParseDate(r.GetString(6)), NullableDate(r, 7)), ct);

        var solutions = await QueryAsync(connection,
            "SELECT Id, Title, Content, SortOrder, EstimatedTime, AlarmId, CreatedAt, UpdatedAt FROM Solutions", r =>
            new Solution(r.GetInt32(0), r.GetString(1), NullableString(r, 2), r.GetInt32(3), NullableString(r, 4),
                r.GetInt32(5), ParseDate(r.GetString(6)), NullableDate(r, 7)), ct);

        // Null when the copy came from a server without shared solutions, so the app hides sharing.
        List<AlarmSolution>? links = null;
        if (await ScalarAsync(connection, null, $"SELECT Value FROM Settings WHERE Key = '{HasAlarmLinksSetting}'", ct) == "true")
            links = await QueryAsync(connection, "SELECT AlarmId, SolutionId FROM AlarmSolutions", r =>
                new AlarmSolution(r.GetInt32(0), r.GetInt32(1)), ct);

        return new SyncSnapshot(DateTime.MinValue, areas, equipment, alarms, solutions, links);
    }

    public async Task<DateTimeOffset?> GetLastSyncedAtAsync(CancellationToken ct = default)
    {
        var value = await GetSettingAsync("LastSyncedAt", ct);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    public async Task<string?> GetSettingAsync(string key, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Settings WHERE Key = $key";
        command.Parameters.AddWithValue("$key", key);
        return await command.ExecuteScalarAsync(ct) as string;
    }

    public async Task SetSettingAsync(string key, string value, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await SetSettingAsync(connection, null, key, value, ct);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    private static Task SetSettingAsync(SqliteConnection connection, SqliteTransaction? tx, string key, string value, CancellationToken ct)
        => ExecuteAsync(connection, tx,
            "INSERT INTO Settings (Key, Value) VALUES ($key, $value) ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value", ct,
            ("$key", key), ("$value", value));

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction? tx, string sql, CancellationToken ct,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static Task<List<T>> QueryAsync<T>(SqliteConnection connection, string sql, Func<SqliteDataReader, T> map, CancellationToken ct)
        => QueryAsync(connection, null, sql, map, ct);

    private static async Task<string?> ScalarAsync(SqliteConnection connection, SqliteTransaction? tx, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(ct) as string;
    }

    private static async Task<List<T>> QueryAsync<T>(SqliteConnection connection, SqliteTransaction? tx, string sql, Func<SqliteDataReader, T> map, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<T>();
        while (await reader.ReadAsync(ct)) result.Add(map(reader));
        return result;
    }

    private static string? NullableString(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static DateTime? NullableDate(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : ParseDate(r.GetString(i));
    private static DateTime ParseDate(string s) => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static string? ToText(DateTime? d) => d?.ToString("O", CultureInfo.InvariantCulture);
}
