using Larmcentralen.Domain.Entities;

namespace Larmcentralen.Domain.Interfaces;

public interface IAreaRepository : IRepository<Area>
{
    Task<Area?> GetByIdWithEquipmentAsync(int id);
    Task<List<Area>> GetAllWithEquipmentAsync();

    /// <summary>Names of avdelningar in the bin (Papperskorg). The database keeps names unique, binned ones included.</summary>
    Task<List<string>> GetBinnedTitlesAsync();
}