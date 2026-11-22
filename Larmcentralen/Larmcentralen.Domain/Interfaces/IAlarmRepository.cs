using Larmcentralen.Domain.Entities;

namespace Larmcentralen.Domain.Interfaces;

public interface IAlarmRepository : IRepository<Alarm>
{
    Task<List<Alarm>> SearchAsync(string? search, int? equipmentId, int? areaId, string? severity, int skip = 0, int take = 10);
    Task<Alarm?> GetByIdWithDetailsAsync(int id);
    /// <summary>True when the unit already has an alarm with this code (other than <paramref name="exceptAlarmId"/>).</summary>
    Task<bool> ExistsAsync(int alarmCodeId, int equipmentId, int? exceptAlarmId = null);
    Task<List<Alarm>> GetByIdsAsync(List<int> ids);

    /// <summary>The alarm with this code on the unit with this designation (both compared case-insensitively), or null.</summary>
    Task<Alarm?> FindByCodeAsync(string equipmentTitle, string code);
}