namespace Larmcentralen.Application.DTOs;

public record EquipmentDto(
    int Id,
    string Title,
    string? DisplayName,
    string? Category,
    string? Description,
    int AreaId,
    string AreaTitle,
    int AlarmCount,
    string? EquipmentType = null
);

public record CreateEquipmentDto(
    string Title,
    string? DisplayName,
    string? Category,
    string? Description,
    int AreaId,
    string? EquipmentType = null
);

/// <summary>
/// Changes a unit. <see cref="EquipmentType"/> null leaves the type as it is (older clients
/// don't send it); an empty string clears it.
/// </summary>
public record UpdateEquipmentDto(
    string Title,
    string? DisplayName,
    string? Category,
    string? Description,
    int AreaId,
    string? EquipmentType = null
);
