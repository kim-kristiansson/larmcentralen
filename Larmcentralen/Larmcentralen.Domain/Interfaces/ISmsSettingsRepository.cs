using Larmcentralen.Domain.Entities;

namespace Larmcentralen.Domain.Interfaces;

public interface ISmsSettingsRepository
{
    /// <summary>The settings row, tracked. Created (empty) the first time.</summary>
    Task<SmsSettings> GetAsync();

    Task SaveChangesAsync();

    /// <summary>Keeps an SMS that didn't match, and drops the oldest beyond <see cref="UnrecognisedSms.MaxKept"/>.</summary>
    Task AddUnrecognisedAsync(UnrecognisedSms sms);

    /// <summary>Newest first.</summary>
    Task<List<UnrecognisedSms>> GetUnrecognisedAsync();

    /// <summary>Removes one (by id) or, when null, all of them. False when the one wasn't there.</summary>
    Task<bool> DeleteUnrecognisedAsync(int? id);
}
