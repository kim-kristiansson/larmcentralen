using Larmcentralen.Domain.Entities;
using Larmcentralen.Domain.Interfaces;
using Larmcentralen.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Larmcentralen.Infrastructure.Repositories;

public class AlarmDefinitionRepository(AppDbContext db) : Repository<AlarmDefinition>(db), IAlarmDefinitionRepository
{
    public async Task<AlarmDefinition?> FindAsync(string equipmentType, string? alarmCode, string normalizedTitle, int scopeEquipmentId)
    {
        // Also look at definitions added in this unit of work but not saved yet.
        var local = Db.AlarmDefinitions.Local.FirstOrDefault(d =>
            d.EquipmentType == equipmentType && d.AlarmCode == alarmCode &&
            d.NormalizedTitle == normalizedTitle && d.ScopeEquipmentId == scopeEquipmentId);
        if (local is not null) return local;

        return await Db.AlarmDefinitions
            .Include(d => d.Solutions)
            .FirstOrDefaultAsync(d =>
                d.EquipmentType == equipmentType && d.AlarmCode == alarmCode &&
                d.NormalizedTitle == normalizedTitle && d.ScopeEquipmentId == scopeEquipmentId);
    }

    public async Task<AlarmDefinition?> GetByIdWithDetailsAsync(int id)
    {
        return await Db.AlarmDefinitions
            .Include(d => d.Alarms).ThenInclude(a => a.Equipment)
            .Include(d => d.Solutions).ThenInclude(s => s.Definitions).ThenInclude(x => x.Alarms)
            .FirstOrDefaultAsync(d => d.Id == id);
    }

    public async Task<List<AlarmDefinition>> GetByIdsAsync(IReadOnlyCollection<int> ids)
    {
        return await Db.AlarmDefinitions
            .Include(d => d.Alarms)
            .Where(d => ids.Contains(d.Id))
            .ToListAsync();
    }

    public async Task<List<DefinitionSolution>> GetAllLinksAsync()
    {
        return await Db.DefinitionSolutions
            .AsNoTracking()
            .OrderBy(l => l.SolutionId)
            .ThenBy(l => l.AlarmDefinitionId)
            .ToListAsync();
    }
}
