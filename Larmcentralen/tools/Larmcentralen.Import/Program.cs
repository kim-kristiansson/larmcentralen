using System.Text.Json;
using Larmcentralen.Application.DTOs;
using Larmcentralen.Application.Services;
using Larmcentralen.Domain.Entities;
using Larmcentralen.Infrastructure.Data;
using Larmcentralen.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

// Imports an alarm list (JSON) into a Larmcentralen SQLite database.
// The data file holds real plant data: keep it outside the repository.
//
// Usage:
//   dotnet run --project tools/Larmcentralen.Import -- <alarms.json> --db <path-to.db> [--area "<title>"]
//       [--types <types.json>] [--remove <unit>[,<unit>...]] [--restore-text] [--dry-run]
//
// --types sets each unit's EquipmentType from a file like { "Härdugn": ["T101", "T102"], "Tvätt": ["W1"] }.
//         Keep it next to the alarm list, outside the repository.
// --restore-text sets the text of alarms already in the database to the text in the alarm list for their unit
//          (for example after alarm definitions had merged the texts of several units into one).
// --remove skips these units in the alarm list and deletes them from the database with their alarms,
//          and the solutions only those alarms had. Their codes stay in the code list.
// --dry-run does everything in a transaction and rolls it back.
//
// Each code is added once to the shared code list; each alarm gets the text from the list for its own unit.
//
// JSON shape: [ { "equipment": "T101", "alarms": [ { "code": "M200.0", "text": "...", "type": "Type 1" } ] } ]
// Running it again is safe: existing areas, equipment and alarms are matched and left as they are.

var options = ImportOptions.Parse(args);
if (options is null)
{
    Console.Error.WriteLine("""
        Usage: Larmcentralen.Import <alarms.json> --db <path-to.db> [--area "<title>"]
               [--types <types.json>] [--remove <unit>[,<unit>...]] [--restore-text] [--dry-run]
        """);
    return 1;
}

var units = JsonSerializer.Deserialize<List<UnitRow>>(
    await File.ReadAllTextAsync(options.JsonPath),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
var types = options.TypesPath is null
    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    : (JsonSerializer.Deserialize<Dictionary<string, List<string>>>(await File.ReadAllTextAsync(options.TypesPath)) ?? [])
        .SelectMany(kv => kv.Value.Select(unit => (unit, type: kv.Key)))
        .ToDictionary(x => x.unit, x => x.type, StringComparer.OrdinalIgnoreCase);
var remove = options.Remove.ToHashSet(StringComparer.OrdinalIgnoreCase);

var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlite($"Data Source={options.DbPath}")
    .AddInterceptors(new SqliteConnectionInterceptor())
    .Options;
await using var db = new AppDbContext(dbOptions);
await SqliteSetup.InitialiseAsync(db);

// The app's own services, so codes and alarms follow exactly the same rules as edits in the app.
var alarmRepo = new AlarmRepository(db);
var equipmentRepo = new EquipmentRepository(db);
var codeRepo = new AlarmCodeRepository(db);
var solutionRepo = new SolutionRepository(db);
var equipmentService = new EquipmentService(equipmentRepo, new AreaRepository(db));
var alarmService = new AlarmService(alarmRepo, codeRepo, equipmentRepo, solutionRepo);

Console.WriteLine($"Before: {await CountsAsync()}");
await using var tx = await db.Database.BeginTransactionAsync();

var area = await db.Areas.FirstOrDefaultAsync(a => a.Title == options.AreaTitle);
if (area is null)
{
    area = new Area { Title = options.AreaTitle, Description = "Larm från OCSS-systemet." };
    db.Areas.Add(area);
    await db.SaveChangesAsync();
    Console.WriteLine($"+ area   {area.Title}");
}

int addedEquipment = 0, addedAlarms = 0, skipped = 0, existing = 0, typed = 0, removedUnits = 0, removedAlarms = 0, restored = 0;

// 1. Unit types (they decide which units count as the same kind, for related alarms and suggestions).
foreach (var equipment in await db.Equipment.Where(e => e.AreaId == area.Id).ToListAsync())
{
    if (!types.TryGetValue(equipment.Title, out var type) || equipment.EquipmentType == type) continue;
    await equipmentService.UpdateAsync(equipment.Id, new UpdateEquipmentDto(
        equipment.Title, equipment.DisplayName, equipment.Category, equipment.Description, equipment.AreaId, type));
    typed++;
    Console.WriteLine($"~ type   {equipment.Title} = {type}");
}

// 2. Units that no longer exist, with their alarms and what only they used.
foreach (var equipment in await db.Equipment.Include(e => e.Alarms).Where(e => e.AreaId == area.Id).ToListAsync())
{
    if (!remove.Contains(equipment.Title)) continue;
    var alarmIds = equipment.Alarms.Select(a => a.Id).ToList();
    foreach (var id in alarmIds) await alarmService.DeleteAsync(id);
    await equipmentService.DeleteAsync(equipment.Id);
    removedUnits++;
    removedAlarms += alarmIds.Count;
    Console.WriteLine($"- unit   {equipment.Title} ({alarmIds.Count} alarms)");
}

// 3. The alarm list.
foreach (var unit in units)
{
    if (remove.Contains(unit.Equipment)) continue;

    var equipment = await db.Equipment.Include(e => e.Alarms).ThenInclude(a => a.AlarmCode)
        .FirstOrDefaultAsync(e => e.AreaId == area.Id && e.Title == unit.Equipment);
    if (equipment is null)
    {
        equipment = new Equipment
        {
            Title = unit.Equipment, Category = "OCSS", AreaId = area.Id,
            EquipmentType = types.GetValueOrDefault(unit.Equipment)
        };
        db.Equipment.Add(equipment);
        await db.SaveChangesAsync();
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

        var code = AlarmText.NormaliseCode(row.Code);
        var key = AlarmText.NormaliseTitle(text);
        var match = equipment.Alarms.FirstOrDefault(a =>
            code is not null ? a.AlarmCode?.Code == code : a.AlarmCode is null && AlarmText.NormaliseTitle(a.Title) == key);
        if (match is not null)
        {
            if (options.RestoreText && match.Title != text)
            {
                Console.WriteLine($"~ text   {unit.Equipment} {code ?? "-"} '{match.Title}' -> '{text}'");
                match.Title = text;
                match.UpdatedAt = DateTime.UtcNow;
                restored++;
            }
            existing++;
            continue;
        }

        var result = await alarmService.CreateAsync(new CreateAlarmDto(
            text, code, "Låg", row.Type is null ? null : $"Störtyp i OCSS: {row.Type}.", equipment.Id));
        if (result.Status != WriteStatus.Ok)
        {
            Console.WriteLine($"! alarm  {unit.Equipment} {code ?? "-"} {text}: {result.Error}");
            continue;
        }
        addedAlarms++;
        Console.WriteLine($"+ alarm  {unit.Equipment} {code ?? "-"} {text}");
    }
}

Console.WriteLine();
Console.WriteLine($"New units: {addedEquipment}, new alarms: {addedAlarms}, already there: {existing}, skipped rows: {skipped}");
Console.WriteLine($"Types set: {typed}, removed units: {removedUnits}, removed alarms: {removedAlarms}");
if (options.RestoreText) Console.WriteLine($"Texts restored: {restored}");
await db.SaveChangesAsync();
Console.WriteLine($"After:  {await CountsAsync()}");

if (options.DryRun)
{
    await tx.RollbackAsync();
    Console.WriteLine("Dry run: rolled back, nothing was saved.");
    return 0;
}

await tx.CommitAsync();
Console.WriteLine($"Saved to {Path.GetFullPath(options.DbPath)}");
return 0;

async Task<string> CountsAsync() =>
    $"units {await db.Equipment.CountAsync()}, alarms {await db.Alarms.CountAsync()}, " +
    $"codes {await db.AlarmCodes.CountAsync()}, solutions {await db.Solutions.CountAsync()}, " +
    $"links {await db.AlarmSolutions.CountAsync()}";

internal sealed record UnitRow(string Equipment, List<AlarmRow> Alarms);

internal sealed record AlarmRow(string? Code, string? Text, string? Type);

internal sealed record ImportOptions(string JsonPath, string DbPath, string AreaTitle, bool DryRun, string? TypesPath,
    IReadOnlyList<string> Remove, bool RestoreText)
{
    public static ImportOptions? Parse(string[] args)
    {
        string? json = null, db = null, area = "SQ-linjen (OCSS)";
        string? types = null;
        var remove = new List<string>();
        var dryRun = false;
        var restoreText = false;
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
                case "--restore-text": restoreText = true; break;
                default:
                    if (args[i].StartsWith("--")) return null;
                    json = args[i];
                    break;
            }
        }
        return json is null || db is null ? null : new ImportOptions(json, db, area, dryRun, types, remove, restoreText);
    }
}
