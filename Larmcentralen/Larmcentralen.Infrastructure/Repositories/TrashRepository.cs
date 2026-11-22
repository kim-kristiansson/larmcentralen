using Larmcentralen.Domain.Entities;
using Larmcentralen.Domain.Interfaces;
using Larmcentralen.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Larmcentralen.Infrastructure.Repositories;

public class TrashRepository(AppDbContext db) : ITrashRepository
{
    public async Task<TrashContents?> CollectAsync(int? areaId, int? equipmentId)
    {
        Area? area = null;
        List<Equipment> units;
        if (areaId is { } aid)
        {
            area = await db.Areas.FirstOrDefaultAsync(a => a.Id == aid);
            if (area is null) return null;
            units = await db.Equipment.Where(e => e.AreaId == aid).ToListAsync();
        }
        else
        {
            var unit = await db.Equipment.FirstOrDefaultAsync(e => e.Id == equipmentId);
            if (unit is null) return null;
            units = [unit];
        }

        var unitIds = units.Select(u => u.Id).ToList();
        var alarms = await db.Alarms.Where(a => unitIds.Contains(a.EquipmentId)).ToListAsync();
        var alarmIds = alarms.Select(a => a.Id).ToHashSet();

        // A solution goes with them only when every alarm it is linked to (binned ones included) is going, or is
        // already in the bin. Looks past the filters, so links to alarms binned earlier still count.
        var links = await db.AlarmSolutions.IgnoreQueryFilters()
            .Where(l => db.AlarmSolutions.IgnoreQueryFilters().Any(x => x.SolutionId == l.SolutionId && alarmIds.Contains(x.AlarmId)))
            .Select(l => new { l.SolutionId, l.AlarmId, Binned = l.Alarm.TrashBatchId != null })
            .ToListAsync();
        var solutionIds = links.GroupBy(l => l.SolutionId)
            .Where(g => g.All(l => alarmIds.Contains(l.AlarmId) || l.Binned))
            .Select(g => g.Key)
            .ToList();
        var solutions = await db.Solutions.Where(s => solutionIds.Contains(s.Id)).ToListAsync();

        return new TrashContents(area, units, alarms, solutions);
    }

    public async Task<TrashContents> GetContentsAsync(int batchId)
        => new(
            await db.Areas.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.TrashBatchId == batchId),
            await db.Equipment.IgnoreQueryFilters().Where(e => e.TrashBatchId == batchId).ToListAsync(),
            await db.Alarms.IgnoreQueryFilters().Where(a => a.TrashBatchId == batchId).ToListAsync(),
            await db.Solutions.IgnoreQueryFilters().Where(s => s.TrashBatchId == batchId).ToListAsync());

    public Task<List<TrashBatch>> GetBatchesAsync()
        => db.TrashBatches.OrderByDescending(b => b.DeletedAt).ThenByDescending(b => b.Id).ToListAsync();

    public Task<TrashBatch?> GetBatchAsync(int id) => db.TrashBatches.FirstOrDefaultAsync(b => b.Id == id);

    public Task<List<TrashBatch>> GetUnitBatchesInAreaAsync(int areaId)
    {
        var batchIds = db.Equipment.IgnoreQueryFilters()
            .Where(e => e.AreaId == areaId && e.TrashBatchId != null)
            .Select(e => e.TrashBatchId!.Value);
        return db.TrashBatches.Where(b => b.Kind == TrashBatch.KindEquipment && batchIds.Contains(b.Id)).ToListAsync();
    }

    public Task<bool> IsAreaBinnedAsync(int areaId)
        => db.Areas.IgnoreQueryFilters().AnyAsync(a => a.Id == areaId && a.TrashBatchId != null);

    public void AddBatch(TrashBatch batch) => db.TrashBatches.Add(batch);

    public async Task EraseAsync(TrashBatch batch)
    {
        var contents = await GetContentsAsync(batch.Id);
        // Children before parents: the foreign keys between them restrict deletes. Links cascade.
        db.Solutions.RemoveRange(contents.Solutions);
        db.Alarms.RemoveRange(contents.Alarms);
        await db.SaveChangesAsync();
        db.Equipment.RemoveRange(contents.Equipment);
        await db.SaveChangesAsync();
        if (contents.Area is not null) db.Areas.Remove(contents.Area);
        db.TrashBatches.Remove(batch);
        await db.SaveChangesAsync();
    }

    public Task SaveChangesAsync() => db.SaveChangesAsync();
}
