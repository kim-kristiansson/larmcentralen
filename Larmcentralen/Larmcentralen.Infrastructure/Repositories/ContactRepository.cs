using Larmcentralen.Domain.Entities;
using Larmcentralen.Domain.Interfaces;
using Larmcentralen.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Larmcentralen.Infrastructure.Repositories;

public class ContactRepository(AppDbContext db) : Repository<Contact>(db), IContactRepository
{
    public Task<List<Contact>> GetAllWithRoleAsync()
        => Db.Contacts.Include(c => c.Role).OrderBy(c => c.Name).ToListAsync();

    public Task<Contact?> GetByIdWithRoleAsync(int id)
        => Db.Contacts.Include(c => c.Role).FirstOrDefaultAsync(c => c.Id == id);
}

public class ContactRoleRepository(AppDbContext db) : Repository<ContactRole>(db), IContactRoleRepository
{
    public Task<List<ContactRole>> GetAllWithContactsAsync()
        => Db.ContactRoles.Include(r => r.Contacts).OrderBy(r => r.Name).ToListAsync();

    public Task<ContactRole?> GetByIdWithContactsAsync(int id)
        => Db.ContactRoles.Include(r => r.Contacts).FirstOrDefaultAsync(r => r.Id == id);
}
