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
            .Include(a => a.AlarmCode)
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
                    EF.Functions.Like(a.Title.ToLower(), startsWith, "\\") ||
                    EF.Functions.Like(a.Title.ToLower(), wordStartsWith, "\\") ||
                    (a.AlarmCode != null && (EF.Functions.Like(a.AlarmCode.Code.ToLower(), startsWith, "\\") || EF.Functions.Like(a.AlarmCode.Code.ToLower(), wordStartsWith, "\\"))) ||
                    (a.Description != null && (EF.Functions.Like(a.Description.ToLower(), startsWith, "\\") || EF.Functions.Like(a.Description.ToLower(), wordStartsWith, "\\"))) ||
                    EF.Functions.Like(a.Equipment.Title.ToLower(), startsWith, "\\") ||
                    EF.Functions.Like(a.Equipment.Title.ToLower(), wordStartsWith, "\\") ||
                    (a.Equipment.DisplayName != null && (EF.Functions.Like(a.Equipment.DisplayName.ToLower(), startsWith, "\\") || EF.Functions.Like(a.Equipment.DisplayName.ToLower(), wordStartsWith, "\\"))) ||
                    EF.Functions.Like(a.Equipment.Area.Title.ToLower(), startsWith, "\\") ||
                    EF.Functions.Like(a.Equipment.Area.Title.ToLower(), wordStartsWith, "\\"));
            }
        }
        
        if (!string.IsNullOrWhiteSpace(severity))
            query = query.Where(a => a.Severity == severity);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var firstTerm = search.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).Last();
            var startsWithPattern = $"{EscapeLike(firstTerm)}%";

            return await query
                .OrderByDescending(a => EF.Functions.Like(a.Title.ToLower(), startsWithPattern, "\\"))
                .ThenBy(a => a.Title)
                .Skip(skip)
                .Take(take)
                .ToListAsync();
        }

        return await query.OrderBy(a => a.Title).Skip(skip).Take(take).ToListAsync();
    }

    public async Task<Alarm?> GetByIdWithDetailsAsync(int id)
    {
        return await Db.Alarms
            .Include(a => a.Equipment)
            .ThenInclude(e => e.Area)
            .Include(a => a.AlarmCode)
            .Include(a => a.Solutions.OrderBy(s => s.SortOrder))
            .ThenInclude(s => s.Alarms)
            .FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<bool> ExistsAsync(int alarmCodeId, int equipmentId, int? exceptAlarmId = null)
    {
        return await Db.Alarms
            .AnyAsync(a => a.AlarmCodeId == alarmCodeId && a.EquipmentId == equipmentId && a.Id != exceptAlarmId);
    }
    
    public async Task<List<Alarm>> GetByIdsAsync(List<int> ids)
    {
        return await Db.Alarms
            .Include(a => a.Equipment)
            .ThenInclude(e => e.Area)
            .Include(a => a.AlarmCode)
            .Where(a => ids.Contains(a.Id))
            .ToListAsync();
    }

    public async Task<Alarm?> FindByCodeAsync(string equipmentTitle, string code)
    {
        var normalisedCode = AlarmText.NormaliseCode(code);
        var unit = equipmentTitle.Trim().ToLowerInvariant();
        if (normalisedCode is null || unit.Length == 0) return null;

        return await Db.Alarms
            .Include(a => a.Equipment)
            .ThenInclude(e => e.Area)
            .Include(a => a.AlarmCode)
            .FirstOrDefaultAsync(a => a.AlarmCode != null && a.AlarmCode.Code == normalisedCode
                                      && a.Equipment.Title.ToLower() == unit);
    }

    private static string EscapeLike(string term) =>
        term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
