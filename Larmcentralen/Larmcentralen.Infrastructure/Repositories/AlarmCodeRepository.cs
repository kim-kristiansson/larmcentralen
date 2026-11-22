using Larmcentralen.Domain.Entities;
using Larmcentralen.Domain.Interfaces;
using Larmcentralen.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Larmcentralen.Infrastructure.Repositories;

public class AlarmCodeRepository(AppDbContext db) : Repository<AlarmCode>(db), IAlarmCodeRepository
{
    public async Task<AlarmCode?> FindAsync(string code)
    {
        var normalised = AlarmText.NormaliseCode(code);
        if (normalised is null) return null;
        return Db.AlarmCodes.Local.FirstOrDefault(c => c.Code == normalised)
               ?? await Db.AlarmCodes.FirstOrDefaultAsync(c => c.Code == normalised);
    }

    public async Task<List<AlarmCode>> GetAllWithAlarmsAsync()
        => await Db.AlarmCodes.Include(c => c.Alarms).OrderBy(c => c.Code).ToListAsync();
}
