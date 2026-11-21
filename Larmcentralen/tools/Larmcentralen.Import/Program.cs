using System.Text.Json;
using Larmcentralen.Domain.Entities;
using Larmcentralen.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

// Imports an alarm list (JSON) into a Larmcentralen SQLite database.
// The data file holds real plant data: keep it outside the repository.
//
// Usage:
//   dotnet run --project tools/Larmcentralen.Import -- <alarms.json> --db <path-to.db> [--area "<title>"]
//       [--types <types.json>] [--remove <unit>[,<unit>...]] [--dry-run]
//
// --types sets each unit's EquipmentType from a file like { "Härdugn": ["T101", "T102"], "Tvätt": ["W1"] }.
//         Keep it next to the alarm list, outside the repository.
// --remove skips these units in the alarm list and deletes them from the database with their alarms
//          and the solutions only their alarms use (shared solutions stay for the other alarms).
//
// JSON shape: [ { "equipment": "T101", "alarms": [ { "code": "M200.0", "text": "...", "type": "Type 1" } ] } ]
// Running it again is safe: existing areas, equipment and alarms are matched and left as they are.

var options = ImportOptions.Parse(args);
if (options is null)
{
    Console.Error.WriteLine("""
        Usage: Larmcentralen.Import <alarms.json> --db <path-to.db> [--area "<title>"]
               [--types <types.json>] [--remove <unit>[,<unit>...]] [--dry-run]
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

var area = await db.Areas.Include(a => a.Equipment).ThenInclude(e => e.Alarms).ThenInclude(a => a.Solutions).ThenInclude(s => s.Alarms)
    .FirstOrDefaultAsync(a => a.Title == options.AreaTitle);
if (area is null)
{
    area = new Area { Title = options.AreaTitle, Description = "Larm från OCSS-systemet." };
    db.Areas.Add(area);
    Console.WriteLine($"+ area   {area.Title}");
}

var types = options.TypesPath is null
    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    : (JsonSerializer.Deserialize<Dictionary<string, List<string>>>(await File.ReadAllTextAsync(options.TypesPath)) ?? [])
        .SelectMany(kv => kv.Value.Select(unit => (unit, type: kv.Key)))
        .ToDictionary(x => x.unit, x => x.type, StringComparer.OrdinalIgnoreCase);
var remove = options.Remove.ToHashSet(StringComparer.OrdinalIgnoreCase);

int addedEquipment = 0, addedAlarms = 0, skipped = 0, existing = 0;

foreach (var unit in units)
{
    if (remove.Contains(unit.Equipment)) continue;

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

int typed = 0, removedUnits = 0, removedAlarms = 0, removedSolutions = 0;
foreach (var equipment in area.Equipment)
{
    if (!types.TryGetValue(equipment.Title, out var type) || equipment.EquipmentType == type) continue;
    equipment.EquipmentType = type;
    equipment.UpdatedAt = DateTime.UtcNow;
    typed++;
    Console.WriteLine($"~ type   {equipment.Title} = {type}");
}

foreach (var equipment in area.Equipment.Where(e => remove.Contains(e.Title)).ToList())
{
    var alarmIds = equipment.Alarms.Select(a => a.Id).ToHashSet();
    // A solution always has an alarm: delete the ones only this unit's alarms use.
    var orphans = equipment.Alarms.SelectMany(a => a.Solutions).Distinct()
        .Where(s => s.Alarms.All(a => alarmIds.Contains(a.Id))).ToList();
    db.Solutions.RemoveRange(orphans);
    db.Alarms.RemoveRange(equipment.Alarms);
    db.Equipment.Remove(equipment);
    removedUnits++;
    removedAlarms += equipment.Alarms.Count;
    removedSolutions += orphans.Count;
    Console.WriteLine($"- unit   {equipment.Title} ({equipment.Alarms.Count} alarms, {orphans.Count} solutions)");
}

Console.WriteLine();
Console.WriteLine($"New units: {addedEquipment}, new alarms: {addedAlarms}, already there: {existing}, skipped rows: {skipped}");
Console.WriteLine($"Types set: {typed}, removed units: {removedUnits}, removed alarms: {removedAlarms}, removed solutions: {removedSolutions}");

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

internal sealed record ImportOptions(string JsonPath, string DbPath, string AreaTitle, bool DryRun, string? TypesPath, IReadOnlyList<string> Remove)
{
    public static ImportOptions? Parse(string[] args)
    {
        string? json = null, db = null, area = "SQ-linjen (OCSS)";
        string? types = null;
        var remove = new List<string>();
        var dryRun = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--db" when i + 1 < args.Length: db = args[++i]; break;
                case "--area" when i + 1 < args.Length: area = args[++i]; break;
                case "--types" when i + 1 < args.Length: types = args[++i]; break;
                case "--remove" when i + 1 < args.Length:
                    remove.AddRange(args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;
                case "--dry-run": dryRun = true; break;
                default:
                    if (args[i].StartsWith("--")) return null;
                    json = args[i];
                    break;
            }
        }
        return json is null || db is null ? null : new ImportOptions(json, db, area, dryRun, types, remove);
    }
}
