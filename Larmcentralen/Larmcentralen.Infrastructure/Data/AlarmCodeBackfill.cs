using System.Data.Common;
using Larmcentralen.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Larmcentralen.Infrastructure.Data;

/// <summary>
/// Runs between the AddAlarmCodes and DropAlarmDefinitions migrations. Lists every code once in AlarmCodes, gives
/// each alarm its definition's code, text, severity and description as its own, and expands each definition's
/// solution links to every alarm of that definition (into a stash that DropAlarmDefinitions turns into
/// AlarmSolutions). Safe to run again: alarms that already have their own text are skipped, codes and links are
/// added once.
/// The per-unit spelling of the text was merged into the definition; the import tool's --restore-text puts the
/// original text from the alarm list back afterwards.
/// </summary>
public static class AlarmCodeBackfill
{
    public static async Task RunAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = db.Database.GetDbConnection();
            await using var tx = await connection.BeginTransactionAsync(ct);

            var codes = new List<string>();
            await using (var read = Command(connection, tx,
                "SELECT DISTINCT \"AlarmCode\" FROM \"AlarmDefinitions\" WHERE \"AlarmCode\" IS NOT NULL"))
            await using (var r = await read.ExecuteReaderAsync(ct))
                while (await r.ReadAsync(ct)) codes.Add(r.GetString(0));

            var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF");
            foreach (var code in codes.Select(AlarmText.NormaliseCode).OfType<string>().Distinct())
            {
                await using var insert = Command(connection, tx,
                    "INSERT OR IGNORE INTO \"AlarmCodes\" (\"Code\", \"CreatedAt\") VALUES ($code, $now)",
                    ("$code", code), ("$now", now));
                await insert.ExecuteNonQueryAsync(ct);
            }

            await using (var update = Command(connection, tx, """
                UPDATE "Alarms" SET
                    "Title" = (SELECT d."Title" FROM "AlarmDefinitions" d WHERE d."Id" = "Alarms"."AlarmDefinitionId"),
                    "Severity" = (SELECT d."Severity" FROM "AlarmDefinitions" d WHERE d."Id" = "Alarms"."AlarmDefinitionId"),
                    "Description" = (SELECT d."Description" FROM "AlarmDefinitions" d WHERE d."Id" = "Alarms"."AlarmDefinitionId"),
                    "AlarmCodeId" = (SELECT c."Id" FROM "AlarmDefinitions" d JOIN "AlarmCodes" c ON c."Code" = UPPER(TRIM(d."AlarmCode"))
                                     WHERE d."Id" = "Alarms"."AlarmDefinitionId")
                WHERE "Title" IS NULL
                """))
                await update.ExecuteNonQueryAsync(ct);

            await using (var links = Command(connection, tx, """
                INSERT INTO "_AlarmSolutionStash" ("AlarmId", "SolutionId")
                SELECT a."Id", l."SolutionId" FROM "DefinitionSolutions" l JOIN "Alarms" a ON a."AlarmDefinitionId" = l."AlarmDefinitionId"
                WHERE NOT EXISTS (SELECT 1 FROM "_AlarmSolutionStash" s WHERE s."AlarmId" = a."Id" AND s."SolutionId" = l."SolutionId")
                """))
                await links.ExecuteNonQueryAsync(ct);

            // The next migration allows one alarm per code on a unit. Stop here, before anything is rebuilt, if that
            // does not hold, rather than half-migrate.
            var duplicates = new List<string>();
            await using (var check = Command(connection, tx, """
                SELECT e."Title", c."Code" FROM "Alarms" a JOIN "Equipment" e ON e."Id" = a."EquipmentId" JOIN "AlarmCodes" c ON c."Id" = a."AlarmCodeId"
                GROUP BY a."EquipmentId", a."AlarmCodeId" HAVING COUNT(*) > 1
                """))
            await using (var r = await check.ExecuteReaderAsync(ct))
                while (await r.ReadAsync(ct)) duplicates.Add($"{r.GetString(0)} {r.GetString(1)}");
            if (duplicates.Count > 0)
                throw new InvalidOperationException(
                    $"Cannot move to alarm codes: these units have the same code on more than one alarm: {string.Join(", ", duplicates)}.");

            await tx.CommitAsync(ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
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
