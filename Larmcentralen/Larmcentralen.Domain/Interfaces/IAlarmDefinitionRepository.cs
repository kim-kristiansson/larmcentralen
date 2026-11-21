using Larmcentralen.Domain.Entities;

namespace Larmcentralen.Domain.Interfaces;

public interface IAlarmDefinitionRepository : IRepository<AlarmDefinition>
{
    /// <summary>The definition with this key, tracked. <paramref name="alarmCode"/> and <paramref name="normalizedTitle"/> must already be normalised.</summary>
    Task<AlarmDefinition?> FindAsync(string equipmentType, string? alarmCode, string normalizedTitle, int scopeEquipmentId);

    /// <summary>Tracked, with its alarms (and their units) and its solutions (and their definitions).</summary>
    Task<AlarmDefinition?> GetByIdWithDetailsAsync(int id);

    Task<List<AlarmDefinition>> GetByIdsAsync(IReadOnlyCollection<int> ids);

    /// <summary>Every definition-solution link, for the sync snapshot.</summary>
    Task<List<DefinitionSolution>> GetAllLinksAsync();
}
