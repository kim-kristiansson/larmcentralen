using Larmcentralen.Domain.Entities;

namespace Larmcentralen.Domain.Interfaces;

public interface IAlarmRepository : IRepository<Alarm>
{
    Task<List<Alarm>> SearchAsync(string? search, int? equipmentId, int? areaId, string? severity, int skip = 0, int take = 10);
    Task<Alarm?> GetByIdWithDetailsAsync(int id);
    Task<bool> ExistsAsync(int alarmDefinitionId, int equipmentId);

    /// <summary>Tracked, with its unit and definition (and the definition's alarms and solutions).</summary>
    Task<Alarm?> GetForUpdateAsync(int id);
    Task<List<Alarm>> GetByIdsAsync(List<int> ids);
}