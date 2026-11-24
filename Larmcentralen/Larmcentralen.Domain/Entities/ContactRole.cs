namespace Larmcentralen.Domain.Entities;

/// <summary>
/// What a contact person does, e.g. "Teamledare" or "Underhåll el". Named ContactRole so it isn't mixed up with the
/// API key roles (User/Admin). Names are unique, ignoring case.
/// </summary>
public class ContactRole
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public List<Contact> Contacts { get; set; } = [];
}
