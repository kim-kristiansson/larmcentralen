using Larmcentralen.Application.DTOs;
using Larmcentralen.Application.Interfaces;
using Larmcentralen.Domain.Entities;
using Larmcentralen.Domain.Interfaces;

namespace Larmcentralen.Application.Services;

/// <summary>
/// Contact people and their roles (admin only, not synced to the app's offline copy yet). Messages are in Swedish:
/// the app shows them as they are.
/// </summary>
public class ContactService(IContactRepository contacts, IContactRoleRepository roles) : IContactService
{
    public const int NameMaxLength = 100;
    public const int NoteMaxLength = 200;

    public async Task<List<ContactRoleDto>> GetRolesAsync()
        => (await roles.GetAllWithContactsAsync()).Select(ToDto).ToList();

    public async Task<WriteResult<ContactRoleDto>> CreateRoleAsync(SaveContactRoleDto dto)
    {
        var name = Clean(dto.Name);
        if (await ValidateRoleAsync(name, null) is { } invalid) return invalid;

        var role = new ContactRole { Name = name };
        await roles.AddAsync(role);
        await roles.SaveChangesAsync();
        return WriteResult<ContactRoleDto>.Ok(ToDto(role));
    }

    public async Task<WriteResult<ContactRoleDto>> UpdateRoleAsync(int id, SaveContactRoleDto dto)
    {
        var role = await roles.GetByIdWithContactsAsync(id);
        if (role is null) return WriteResult<ContactRoleDto>.NotFound();
        var name = Clean(dto.Name);
        if (await ValidateRoleAsync(name, id) is { } invalid) return invalid;

        role.Name = name;
        role.UpdatedAt = DateTime.UtcNow;
        await roles.SaveChangesAsync();
        return WriteResult<ContactRoleDto>.Ok(ToDto(role));
    }

    /// <summary>Conflict, with the names of the people as the value, while any contact has the role.</summary>
    public async Task<WriteResult<List<string>>> DeleteRoleAsync(int id)
    {
        var role = await roles.GetByIdWithContactsAsync(id);
        if (role is null) return WriteResult<List<string>>.NotFound();

        var names = role.Contacts.Select(c => c.Name).Order(StringComparer.CurrentCultureIgnoreCase).ToList();
        if (names.Count > 0)
            return new(WriteStatus.Conflict, names,
                $"Rollen {role.Name} används av {JoinNames(names)}. Ge dem en annan roll först.");

        roles.Remove(role);
        await roles.SaveChangesAsync();
        return WriteResult<List<string>>.Ok([]);
    }

    public async Task<List<ContactDto>> GetContactsAsync()
        => (await contacts.GetAllWithRoleAsync()).Select(ToDto).ToList();

    public async Task<ContactDto?> GetContactAsync(int id)
        => await contacts.GetByIdWithRoleAsync(id) is { } c ? ToDto(c) : null;

    public async Task<WriteResult<ContactDto>> CreateContactAsync(SaveContactDto dto)
    {
        var (values, invalid) = await ValidateContactAsync(dto, null);
        if (invalid is not null) return invalid;

        var contact = new Contact { Name = values.Name, Phone = values.Phone, RoleId = values.Role.Id, Role = values.Role, Note = values.Note };
        await contacts.AddAsync(contact);
        await contacts.SaveChangesAsync();
        return WriteResult<ContactDto>.Ok(ToDto(contact));
    }

    public async Task<WriteResult<ContactDto>> UpdateContactAsync(int id, SaveContactDto dto)
    {
        var contact = await contacts.GetByIdWithRoleAsync(id);
        if (contact is null) return WriteResult<ContactDto>.NotFound();
        var (values, invalid) = await ValidateContactAsync(dto, id);
        if (invalid is not null) return invalid;

        contact.Name = values.Name;
        contact.Phone = values.Phone;
        contact.RoleId = values.Role.Id;
        contact.Role = values.Role;
        contact.Note = values.Note;
        contact.UpdatedAt = DateTime.UtcNow;
        await contacts.SaveChangesAsync();
        return WriteResult<ContactDto>.Ok(ToDto(contact));
    }

    public async Task<bool> DeleteContactAsync(int id)
    {
        var contact = await contacts.GetByIdAsync(id);
        if (contact is null) return false;
        contacts.Remove(contact);
        await contacts.SaveChangesAsync();
        return true;
    }

    private async Task<WriteResult<ContactRoleDto>?> ValidateRoleAsync(string name, int? id)
    {
        if (name.Length == 0) return WriteResult<ContactRoleDto>.Invalid("Ange rollens namn.");
        if (name.Length > NameMaxLength) return WriteResult<ContactRoleDto>.Invalid($"Namnet får vara högst {NameMaxLength} tecken.");
        if ((await roles.GetAllAsync()).FirstOrDefault(r => r.Id != id && SameName(r.Name, name)) is { } same)
            return WriteResult<ContactRoleDto>.Conflict($"Det finns redan en roll som heter {Clean(same.Name)}.");
        return null;
    }

    private async Task<((string Name, string Phone, ContactRole Role, string? Note) Values, WriteResult<ContactDto>? Error)> ValidateContactAsync(SaveContactDto dto, int? id)
    {
        static WriteResult<ContactDto> Invalid(string error) => WriteResult<ContactDto>.Invalid(error);

        var name = Clean(dto.Name);
        if (name.Length == 0) return (default, Invalid("Ange namnet."));
        if (name.Length > NameMaxLength) return (default, Invalid($"Namnet får vara högst {NameMaxLength} tecken."));

        if (string.IsNullOrWhiteSpace(dto.Phone)) return (default, Invalid("Ange telefonnumret."));
        if (SwedishPhoneNumber.Normalise(dto.Phone) is not { } phone)
            return (default, Invalid("Telefonnumret ser inte ut som ett svenskt nummer. Skriv det till exempel 070-123 45 67."));

        if (dto.RoleId <= 0) return (default, Invalid("Välj en roll."));
        if (await roles.GetByIdAsync(dto.RoleId) is not { } role) return (default, Invalid("Rollen finns inte längre. Välj en annan."));

        var note = AreaService.Clean(dto.Note);
        if (note?.Length > NoteMaxLength) return (default, Invalid($"Anmärkningen får vara högst {NoteMaxLength} tecken."));

        // The same person twice, or two people on one number, is a mistake: the company has no shared numbers.
        var others = (await contacts.GetAllAsync()).Where(c => c.Id != id).ToList();
        if (others.FirstOrDefault(c => SameName(c.Name, name)) is { } sameName)
            return (default, WriteResult<ContactDto>.Conflict($"Det finns redan en kontakt som heter {Clean(sameName.Name)}."));
        if (others.FirstOrDefault(c => SwedishPhoneNumber.SameNumber(c.Phone, phone)) is { } sameNumber)
            return (default, WriteResult<ContactDto>.Conflict($"Numret används redan av {Clean(sameNumber.Name)}."));

        return ((name, phone, role, note), null);
    }

    /// <summary>"Anna", "Anna och Erik", "Anna, Erik och Sara".</summary>
    internal static string JoinNames(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "",
        1 => names[0],
        _ => string.Join(", ", names.Take(names.Count - 1)) + " och " + names[^1]
    };

    /// <summary>Trimmed, with runs of spaces inside made one: "  Anna   Berg " is "Anna Berg".</summary>
    internal static string Clean(string? text) => string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The same name however it is spaced or cased ("anna  berg" is "Anna Berg"), å, ä and ö included.</summary>
    internal static bool SameName(string? a, string? b) => string.Equals(Clean(a), Clean(b), StringComparison.CurrentCultureIgnoreCase);

    private static ContactRoleDto ToDto(ContactRole r) => new(r.Id, r.Name, r.Contacts.Count);

    private static ContactDto ToDto(Contact c) => new(c.Id, c.Name, c.Phone, c.RoleId, c.Role.Name, c.Note);
}
