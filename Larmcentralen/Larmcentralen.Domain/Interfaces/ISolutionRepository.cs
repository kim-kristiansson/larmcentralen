using Larmcentralen.Domain.Entities;

namespace Larmcentralen.Domain.Interfaces;

public interface ISolutionRepository : IRepository<Solution>
{
    Task<List<Solution>> GetByAlarmIdAsync(int alarmId);

    /// <summary>The solution with its linked alarms loaded (tracked, so the links can be changed).</summary>
    Task<Solution?> GetByIdWithDetailsAsync(int id);

    /// <summary>Every alarm-solution link, for the sync snapshot.</summary>
    Task<List<AlarmSolution>> GetAllLinksAsync();
}
