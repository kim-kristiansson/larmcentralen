namespace Larmcentralen.Application.DTOs;

public record AreaDto(
    int Id,
    string Title,
    string? DisplayName,
    string? Description,
    string? Location,
    int EquipmentCount
);

public record CreateAreaDto(
    string Title,
    string? DisplayName,
    string? Description,
    string? Location
);

/// <summary>Changes an area. For the optional fields, null leaves the value as it is and an empty string clears it.</summary>
public record UpdateAreaDto(
    string Title,
    string? DisplayName,
    string? Description,
    string? Location
);
