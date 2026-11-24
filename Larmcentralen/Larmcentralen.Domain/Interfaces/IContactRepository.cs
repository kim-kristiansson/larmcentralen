using Larmcentralen.Domain.Entities;

namespace Larmcentralen.Domain.Interfaces;

public interface IContactRepository : IRepository<Contact>
{
    /// <summary>Every contact with its role, by name.</summary>
    Task<List<Contact>> GetAllWithRoleAsync();
    Task<Contact?> GetByIdWithRoleAsync(int id);
}

public interface IContactRoleRepository : IRepository<ContactRole>
{
    /// <summary>Every role with the contacts that have it, by name.</summary>
    Task<List<ContactRole>> GetAllWithContactsAsync();
    Task<ContactRole?> GetByIdWithContactsAsync(int id);
}
