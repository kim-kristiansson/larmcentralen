using Larmcentralen.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

// One-time copy of the old PostgreSQL database into the SQLite file the API now uses.
// Usage:
//   dotnet run --project tools/Larmcentralen.PostgresImport -- "<postgres connection string>" "<sqlite connection string>"
// Example:
//   dotnet run --project tools/Larmcentralen.PostgresImport -- \
//     "Host=localhost;Database=larm;Username=larm;Password=..." \
//     "Data Source=Larmcentralen.Api/data/larmcentralen.db"
//
// Ids are kept, so links between areas, equipment, alarms and solutions stay the same.
// The target must be empty; stop the API while importing.

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: Larmcentralen.PostgresImport \"<postgres connection string>\" \"<sqlite connection string>\"");
    return 1;
}

var source = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
    .UseNpgsql(args[0])
    .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
    .Options);

await using var target = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlite(args[1])
    .AddInterceptors(new SqliteConnectionInterceptor())
    .Options);

await SqliteSetup.InitialiseAsync(target);

if (await target.Areas.AnyAsync() || await target.Equipment.AnyAsync() ||
    await target.Alarms.AnyAsync() || await target.Solutions.AnyAsync())
{
    Console.Error.WriteLine("The SQLite database already has data. Point at a new file, or delete it first.");
    return 2;
}

await using (source)
{
    var areas = await source.Areas.ToListAsync();
    var equipment = await source.Equipment.ToListAsync();
    var alarms = await source.Alarms.ToListAsync();
    var solutions = await source.Solutions.ToListAsync();

    await using var tx = await target.Database.BeginTransactionAsync();
    target.Areas.AddRange(areas);
    target.Equipment.AddRange(equipment);
    target.Alarms.AddRange(alarms);
    target.Solutions.AddRange(solutions);
    await target.SaveChangesAsync();
    await tx.CommitAsync();

    Console.WriteLine($"Imported {areas.Count} areas, {equipment.Count} equipment, " +
                      $"{alarms.Count} alarms and {solutions.Count} solutions.");
}

return 0;
