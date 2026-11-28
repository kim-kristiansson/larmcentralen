using Larmcentralen.Domain.Entities;

namespace Larmcentralen.Domain.Interfaces;

public interface IEquipmentRepository : IRepository<Equipment>
{
    Task<List<Equipment>> GetByAreaIdAsync(int areaId);
    Task<Equipment?> GetByIdWithDetailsAsync(int id);
    Task<Equipment?> GetByTitleAsync(string title);

    /// <summary>Units with this designation (case-insensitive), in one avdelning or, when null, in all of them.</summary>
    Task<List<Equipment>> FindByTitleAsync(string title, int? areaId);
}