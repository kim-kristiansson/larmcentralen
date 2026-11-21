using Larmcentralen.Domain.Entities;

namespace Larmcentralen.Domain.Interfaces;

public interface IAlarmCodeRepository : IRepository<AlarmCode>
{
    /// <summary>The code row for this code (any case, surrounding spaces ignored), including one added but not saved yet.</summary>
    Task<AlarmCode?> FindAsync(string code);

    /// <summary>Every code with its alarms loaded, ordered by code.</summary>
    Task<List<AlarmCode>> GetAllWithAlarmsAsync();
}
