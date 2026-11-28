using Larmcentralen.Domain.Entities;
using Larmcentralen.Domain.Interfaces;
using Larmcentralen.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Larmcentralen.Infrastructure.Repositories;

public class SmsSettingsRepository(AppDbContext db) : ISmsSettingsRepository
{
    public async Task<SmsSettings> GetAsync()
    {
        var settings = await db.SmsSettings.FirstOrDefaultAsync(s => s.Id == SmsSettings.SingletonId);
        if (settings is not null) return settings;

        settings = new SmsSettings();
        db.SmsSettings.Add(settings);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Another request created it at the same moment: use that one.
            db.Entry(settings).State = EntityState.Detached;
            settings = await db.SmsSettings.FirstAsync(s => s.Id == SmsSettings.SingletonId);
        }
        return settings;
    }

    public async Task SaveChangesAsync() => await db.SaveChangesAsync();

    public async Task AddUnrecognisedAsync(UnrecognisedSms sms)
    {
        db.UnrecognisedSms.Add(sms);
        await db.SaveChangesAsync();
        await db.UnrecognisedSms
            .OrderByDescending(s => s.ReceivedAt).ThenByDescending(s => s.Id)
            .Skip(UnrecognisedSms.MaxKept)
            .ExecuteDeleteAsync();
    }

    public async Task<List<UnrecognisedSms>> GetUnrecognisedAsync()
        => await db.UnrecognisedSms.AsNoTracking().OrderByDescending(s => s.ReceivedAt).ThenByDescending(s => s.Id).ToListAsync();

    public async Task<bool> DeleteUnrecognisedAsync(int? id)
    {
        var query = id is null ? db.UnrecognisedSms : db.UnrecognisedSms.Where(s => s.Id == id);
        return await query.ExecuteDeleteAsync() > 0 || id is null;
    }
}
