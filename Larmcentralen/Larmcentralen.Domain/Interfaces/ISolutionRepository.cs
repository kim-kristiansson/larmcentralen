using Larmcentralen.Domain.Entities;

namespace Larmcentralen.Domain.Interfaces;

public interface ISolutionRepository : IRepository<Solution>
{
    /// <summary>The solutions an alarm shows: those linked to its definition.</summary>
    Task<List<Solution>> GetByAlarmIdAsync(int alarmId);

    /// <summary>The solution with its definitions and their alarms loaded (tracked, so the links can be changed).</summary>
    Task<Solution?> GetByIdWithDetailsAsync(int id);
}
