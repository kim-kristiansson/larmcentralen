using Larmcentralen.Domain.Entities;

namespace Larmcentralen.Domain.Interfaces;

public interface ISmsSettingsRepository
{
    /// <summary>The settings row, tracked. Created (empty) the first time.</summary>
    Task<SmsSettings> GetAsync();

    Task SaveChangesAsync();
}
