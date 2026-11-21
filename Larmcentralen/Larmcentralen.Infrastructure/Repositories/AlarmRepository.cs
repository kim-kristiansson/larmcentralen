using Larmcentralen.Domain.Entities;
using Larmcentralen.Domain.Interfaces;
using Larmcentralen.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Larmcentralen.Infrastructure.Repositories;

public class AlarmRepository(AppDbContext db) : Repository<Alarm>(db), IAlarmRepository
{
    public async Task<List<Alarm>> SearchAsync(string? search, int? equipmentId, int? areaId, string? severity, int skip = 0, int take = 10)
    {
        var query = Db.Alarms
            .Include(a => a.Equipment)
            .ThenInclude(e => e.Area)
            .Include(a => a.Definition)
            .AsQueryable();

        if (areaId.HasValue)
            query = query.Where(a => a.Equipment.AreaId == areaId.Value);

        if (equipmentId.HasValue)
            query = query.Where(a => a.EquipmentId == equipmentId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Case-insensitive for å/ä/ö too: lower() is replaced by a Unicode-aware
            // version on every connection (see SqliteConnectionInterceptor).
            var terms = search.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var term in terms)
            {
                var escaped = EscapeLike(term);
                var startsWith = $"{escaped}%";
                var wordStartsWith = $"% {escaped}%";

                query = query.Where(a =>
                    EF.Functions.Like(a.Definition.Title.ToLower(), startsWith, "\\") ||
                    EF.Functions.Like(a.Definition.Title.ToLower(), wordStartsWith, "\\") ||
                    (a.Definition.AlarmCode != null && (EF.Functions.Like(a.Definition.AlarmCode.ToLower(), startsWith, "\\") || EF.Functions.Like(a.Definition.AlarmCode.ToLower(), wordStartsWith, "\\"))) ||
                    (a.Definition.Description != null && (EF.Functions.Like(a.Definition.Description.ToLower(), startsWith, "\\") || EF.Functions.Like(a.Definition.Description.ToLower(), wordStartsWith, "\\"))) ||
                    EF.Functions.Like(a.Equipment.Title.ToLower(), startsWith, "\\") ||
                    EF.Functions.Like(a.Equipment.Title.ToLower(), wordStartsWith, "\\") ||
                    (a.Equipment.DisplayName != null && (EF.Functions.Like(a.Equipment.DisplayName.ToLower(), startsWith, "\\") || EF.Functions.Like(a.Equipment.DisplayName.ToLower(), wordStartsWith, "\\"))) ||
                    EF.Functions.Like(a.Equipment.Area.Title.ToLower(), startsWith, "\\") ||
                    EF.Functions.Like(a.Equipment.Area.Title.ToLower(), wordStartsWith, "\\"));
            }
        }
        
        if (!string.IsNullOrWhiteSpace(severity))
            query = query.Where(a => a.Definition.Severity == severity);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var firstTerm = search.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).Last();
            var startsWithPattern = $"{EscapeLike(firstTerm)}%";

            return await query
                .OrderByDescending(a => EF.Functions.Like(a.Definition.Title.ToLower(), startsWithPattern, "\\"))
                .ThenBy(a => a.Definition.Title)
                .Skip(skip)
                .Take(take)
                .ToListAsync();
        }

        return await query.OrderBy(a => a.Definition.Title).Skip(skip).Take(take).ToListAsync();
    }

    public async Task<Alarm?> GetByIdWithDetailsAsync(int id)
    {
        return await Db.Alarms
            .Include(a => a.Equipment)
            .ThenInclude(e => e.Area)
            .Include(a => a.Definition).ThenInclude(d => d.Alarms).ThenInclude(x => x.Equipment)
            .Include(a => a.Definition).ThenInclude(d => d.Solutions).ThenInclude(s => s.Definitions).ThenInclude(d => d.Alarms)
            .FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<bool> ExistsAsync(int alarmDefinitionId, int equipmentId)
    {
        return await Db.Alarms
            .AnyAsync(a => a.AlarmDefinitionId == alarmDefinitionId && a.EquipmentId == equipmentId);
    }

    public async Task<Alarm?> GetForUpdateAsync(int id)
    {
        return await Db.Alarms
            .Include(a => a.Equipment)
            .Include(a => a.Definition).ThenInclude(d => d.Alarms)
            .Include(a => a.Definition).ThenInclude(d => d.Solutions).ThenInclude(s => s.Definitions)
            .FirstOrDefaultAsync(a => a.Id == id);
    }
    
    public async Task<List<Alarm>> GetByIdsAsync(List<int> ids)
    {
        return await Db.Alarms
            .Include(a => a.Equipment)
            .ThenInclude(e => e.Area)
            .Include(a => a.Definition)
            .Where(a => ids.Contains(a.Id))
            .ToListAsync();
    }

    private static string EscapeLike(string term) =>
        term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
