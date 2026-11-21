using System.Text.Json;
using Larmcentralen.Domain.Entities;
using Larmcentralen.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

// Imports an alarm list (JSON) into a Larmcentralen SQLite database.
// The data file holds real plant data: keep it outside the repository.
//
// Usage:
//   dotnet run --project tools/Larmcentralen.Import -- <alarms.json> --db <path-to.db> [--area "<title>"] [--dry-run]
//
// JSON shape: [ { "equipment": "T101", "alarms": [ { "code": "M200.0", "text": "...", "type": "Type 1" } ] } ]
// Running it again is safe: existing areas, equipment and alarms are matched and left as they are.

var options = ImportOptions.Parse(args);
if (options is null)
{
    Console.Error.WriteLine("""
        Usage: Larmcentralen.Import <alarms.json> --db <path-to.db> [--area "<title>"] [--dry-run]
        """);
    return 1;
}

var units = JsonSerializer.Deserialize<List<UnitRow>>(
    await File.ReadAllTextAsync(options.JsonPath),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlite($"Data Source={options.DbPath}")
    .AddInterceptors(new SqliteConnectionInterceptor())
    .Options;
await using var db = new AppDbContext(dbOptions);
await SqliteSetup.InitialiseAsync(db);

var area = await db.Areas.Include(a => a.Equipment).ThenInclude(e => e.Alarms)
    .FirstOrDefaultAsync(a => a.Title == options.AreaTitle);
if (area is null)
{
    area = new Area { Title = options.AreaTitle, Description = "Larm från OCSS-systemet." };
    db.Areas.Add(area);
    Console.WriteLine($"+ area   {area.Title}");
}

int addedEquipment = 0, addedAlarms = 0, skipped = 0, existing = 0;

foreach (var unit in units)
{
    var equipment = area.Equipment.FirstOrDefault(e => e.Title == unit.Equipment);
    if (equipment is null)
    {
        equipment = new Equipment { Title = unit.Equipment, Category = "OCSS" };
        area.Equipment.Add(equipment);
        addedEquipment++;
        Console.WriteLine($"+ unit   {unit.Equipment}");
    }

    foreach (var row in unit.Alarms)
    {
        var text = row.Text?.Trim();
        // Empty rows are unused PLC bits, and "error" rows are placeholders with no meaning.
        if (string.IsNullOrEmpty(text) || text.Equals("error", StringComparison.OrdinalIgnoreCase))
        {
            skipped++;
            continue;
        }

        var code = string.IsNullOrWhiteSpace(row.Code) ? null : row.Code.Trim();
        var match = equipment.Alarms.FirstOrDefault(a =>
            code is not null ? a.AlarmCode == code : a.AlarmCode is null && a.Title == text);
        if (match is not null)
        {
            existing++;
            continue;
        }

        equipment.Alarms.Add(new Alarm
        {
            Title = text,
            AlarmCode = code,
            Description = row.Type is null ? null : $"Störtyp i OCSS: {row.Type}.",
        });
        addedAlarms++;
        Console.WriteLine($"+ alarm  {unit.Equipment} {code ?? "-"} {text}");
    }
}

Console.WriteLine();
Console.WriteLine($"New units: {addedEquipment}, new alarms: {addedAlarms}, already there: {existing}, skipped rows: {skipped}");

if (options.DryRun)
{
    Console.WriteLine("Dry run: nothing was saved.");
    return 0;
}

await db.SaveChangesAsync();
Console.WriteLine($"Saved to {Path.GetFullPath(options.DbPath)}");
return 0;

internal sealed record UnitRow(string Equipment, List<AlarmRow> Alarms);

internal sealed record AlarmRow(string? Code, string? Text, string? Type);

internal sealed record ImportOptions(string JsonPath, string DbPath, string AreaTitle, bool DryRun)
{
    public static ImportOptions? Parse(string[] args)
    {
        string? json = null, db = null, area = "SQ-linjen (OCSS)";
        var dryRun = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--db" when i + 1 < args.Length: db = args[++i]; break;
                case "--area" when i + 1 < args.Length: area = args[++i]; break;
                case "--dry-run": dryRun = true; break;
                default:
                    if (args[i].StartsWith("--")) return null;
                    json = args[i];
                    break;
            }
        }
        return json is null || db is null ? null : new ImportOptions(json, db, area, dryRun);
    }
}
