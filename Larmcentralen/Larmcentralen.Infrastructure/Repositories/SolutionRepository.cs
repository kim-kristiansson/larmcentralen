using Larmcentralen.Domain.Entities;
using Larmcentralen.Domain.Interfaces;
using Larmcentralen.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Larmcentralen.Infrastructure.Repositories;

public class SolutionRepository(AppDbContext db) : Repository<Solution>(db), ISolutionRepository
{
    public async Task<List<Solution>> GetByAlarmIdAsync(int alarmId)
    {
        return await Db.Solutions
            .Include(s => s.Definitions).ThenInclude(d => d.Alarms)
            .Where(s => s.Definitions.Any(d => d.Alarms.Any(a => a.Id == alarmId)))
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Id)
            .ToListAsync();
    }

    public async Task<Solution?> GetByIdWithDetailsAsync(int id)
    {
        return await Db.Solutions
            .Include(s => s.Definitions).ThenInclude(d => d.Alarms)
            .FirstOrDefaultAsync(s => s.Id == id);
    }
}
