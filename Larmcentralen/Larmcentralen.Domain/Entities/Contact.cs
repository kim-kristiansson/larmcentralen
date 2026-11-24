namespace Larmcentralen.Domain.Entities;

/// <summary>
/// A person operators can call. <see cref="Phone"/> is stored in international form (+46…), see
/// <see cref="SwedishPhoneNumber"/>. <see cref="Note"/> is short, e.g. "vardagar 07–16".
/// </summary>
public class Contact
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public int RoleId { get; set; }
    public ContactRole Role { get; set; } = null!;
}
