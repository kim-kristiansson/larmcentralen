namespace Larmcentralen.Application.DTOs;

/// <summary>What removing an avdelning or a unit would take with it, for the confirmation.</summary>
public record TrashPreviewDto(string Kind, int ItemId, string Title, int EquipmentCount, int AlarmCount, int SolutionCount);

/// <summary>
/// One removal in the bin. <see cref="EraseAt"/> is when it is erased for good unless restored first.
/// <see cref="Kind"/> is "Area" or "Equipment".
/// </summary>
public record TrashItemDto(
    int Id,
    string Kind,
    int ItemId,
    string Title,
    string? AreaTitle,
    int EquipmentCount,
    int AlarmCount,
    int SolutionCount,
    DateTime DeletedAt,
    DateTime EraseAt);
