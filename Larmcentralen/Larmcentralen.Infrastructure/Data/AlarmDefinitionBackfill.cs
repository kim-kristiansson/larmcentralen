using System.Data.Common;
using Larmcentralen.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Larmcentralen.Infrastructure.Data;

/// <summary>
/// Runs between the AddAlarmDefinitions and MoveAlarmTextToDefinitions migrations. Groups every alarm that has
/// no definition yet by (unit type, code, normalised text) — "Övrigt" and untyped units per unit — and gives each
/// group one definition: the most common spelling as its title, the highest severity, the most common
/// description. Then copies each alarm's solution links to its definition. Safe to run again: alarms that
/// already have a definition are skipped and links are inserted only once.
/// </summary>
public static class AlarmDefinitionBackfill
{
    private sealed record Row(int Id, string Title, string? Code, string Severity, string? Description,
        int EquipmentId, string? EquipmentType, string CreatedAt);

    /// <summary>Returns how many definitions were created.</summary>
    public static async Task<int> RunAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var tx = await db.Database.GetDbConnection().BeginTransactionAsync(ct);
            var connection = db.Database.GetDbConnection();

            var rows = new List<Row>();
            await using (var read = Command(connection, tx, """
                SELECT a."Id", a."Title", a."AlarmCode", a."Severity", a."Description", a."EquipmentId", e."EquipmentType", a."CreatedAt"
                FROM "Alarms" a JOIN "Equipment" e ON e."Id" = a."EquipmentId"
                WHERE a."AlarmDefinitionId" IS NULL
                ORDER BY a."Id"
                """))
            await using (var r = await read.ExecuteReaderAsync(ct))
            {
                while (await r.ReadAsync(ct))
                    rows.Add(new Row(r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3),
                        r.IsDBNull(4) ? null : r.GetString(4), r.GetInt32(5), r.IsDBNull(6) ? null : r.GetString(6), r.GetString(7)));
            }

            var created = 0;
            var groups = rows.GroupBy(x =>
            {
                var type = AlarmDefinition.TypeFor(x.EquipmentType);
                return (Type: type, Code: AlarmDefinition.NormaliseCode(x.Code), Key: AlarmDefinition.NormaliseTitle(x.Title),
                    Scope: type == AlarmDefinition.OtherType ? x.EquipmentId : 0);
            });

            foreach (var g in groups)
            {
                var alarms = g.ToList();
                var definitionId = await FindAsync(connection, tx, g.Key.Type, g.Key.Code, g.Key.Key, g.Key.Scope, ct);
                if (definitionId is null)
                {
                    await using var insert = Command(connection, tx, """
                        INSERT INTO "AlarmDefinitions" ("EquipmentType", "ScopeEquipmentId", "AlarmCode", "Title", "NormalizedTitle", "Severity", "Description", "CreatedAt")
                        VALUES ($type, $scope, $code, $title, $key, $severity, $description, $created);
                        SELECT last_insert_rowid();
                        """,
                        ("$type", (object?)g.Key.Type), ("$scope", (object?)g.Key.Scope), ("$code", (object?)g.Key.Code), ("$key", (object?)g.Key.Key),
                        ("$title", (object?)MostCommon(alarms.Select(a => a.Title.Trim()))),
                        ("$severity", (object?)alarms.MaxBy(a => AlarmDefinition.SeverityRank(a.Severity))!.Severity),
                        ("$description", (object?)MostCommon(alarms.Select(a => a.Description).Where(d => !string.IsNullOrWhiteSpace(d)))),
                        ("$created", (object?)alarms.Select(a => a.CreatedAt).Min(StringComparer.Ordinal)));
                    definitionId = Convert.ToInt32(await insert.ExecuteScalarAsync(ct));
                    created++;
                }

                foreach (var a in alarms)
                {
                    await using var update = Command(connection, tx,
                        "UPDATE \"Alarms\" SET \"AlarmDefinitionId\" = $definition WHERE \"Id\" = $id",
                        ("$definition", (object?)definitionId), ("$id", (object?)a.Id));
                    await update.ExecuteNonQueryAsync(ct);
                }
            }

            // Solutions move from alarms to their definitions. Duplicates (two alarms of one definition) collapse.
            if (await TableExistsAsync(connection, tx, "AlarmSolutions", ct))
            {
                await using var links = Command(connection, tx, """
                    INSERT OR IGNORE INTO "DefinitionSolutions" ("AlarmDefinitionId", "SolutionId")
                    SELECT a."AlarmDefinitionId", l."SolutionId" FROM "AlarmSolutions" l JOIN "Alarms" a ON a."Id" = l."AlarmId"
                    WHERE a."AlarmDefinitionId" IS NOT NULL
                    """);
                await links.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return created;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>The value that occurs most often; on a tie, the one seen first.</summary>
    private static string? MostCommon(IEnumerable<string?> values)
        => values.Select((v, i) => (v, i)).GroupBy(x => x.v)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.First().i)
            .Select(g => g.Key).FirstOrDefault();

    private static async Task<int?> FindAsync(DbConnection c, DbTransaction tx, string type, string? code, string key, int scope, CancellationToken ct)
    {
        await using var find = Command(c, tx, """
            SELECT "Id" FROM "AlarmDefinitions"
            WHERE "EquipmentType" = $type AND "AlarmCode" IS $code AND "NormalizedTitle" = $key AND "ScopeEquipmentId" = $scope
            """, ("$type", (object?)type), ("$code", (object?)code), ("$key", (object?)key), ("$scope", (object?)scope));
        var value = await find.ExecuteScalarAsync(ct);
        return value is null or DBNull ? null : Convert.ToInt32(value);
    }

    private static async Task<bool> TableExistsAsync(DbConnection c, DbTransaction tx, string table, CancellationToken ct)
    {
        await using var cmd = Command(c, tx, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name", ("$name", (object?)table));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    private static DbCommand Command(DbConnection c, DbTransaction tx, string sql, params (string Name, object? Value)[] parameters)
    {
        var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }
        return cmd;
    }
}
