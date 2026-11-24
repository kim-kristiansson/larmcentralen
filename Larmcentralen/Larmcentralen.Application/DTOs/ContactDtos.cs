namespace Larmcentralen.Application.DTOs;

/// <summary>A role and how many contacts have it (a role in use can't be removed).</summary>
public record ContactRoleDto(int Id, string Name, int ContactCount);

public record SaveContactRoleDto(string Name);

/// <summary>A contact person. <see cref="Phone"/> is in international form (+46…).</summary>
public record ContactDto(int Id, string Name, string Phone, int RoleId, string RoleName, string? Note);

/// <summary>Create or change a contact. The phone number may be written any Swedish way; it is stored as +46….</summary>
public record SaveContactDto(string Name, string Phone, int RoleId, string? Note);
