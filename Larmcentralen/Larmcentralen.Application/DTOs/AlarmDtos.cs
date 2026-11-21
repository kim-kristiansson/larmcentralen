namespace Larmcentralen.Application.DTOs;

/// <summary>
/// An alarm on one unit. Title, code, severity and description come from its definition, which
/// <see cref="SameDefinitionUnits"/> share (other units of the same type with the same code and text).
/// </summary>
public record AlarmDto(
    int Id,
    string Title,
    string? AlarmCode,
    string Severity,
    string? Description,
    int EquipmentId,
    string EquipmentTitle,
    string AreaTitle,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    List<SolutionDto> Solutions,
    int AlarmDefinitionId = 0,
    List<string>? SameDefinitionUnits = null
);

public record AlarmListDto(
    int Id,
    string Title,
    string? AlarmCode,
    string Severity,
    string EquipmentTitle,
    string AreaTitle
);

/// <summary>A new alarm on a unit. It joins the definition with the same code and text on units of its type, or starts one.</summary>
public record CreateAlarmDto(
    string Title,
    string? AlarmCode,
    string Severity,
    string? Description,
    int EquipmentId
);

/// <summary>
/// Changes an alarm. Title, code, severity and description belong to the definition, so they change
/// for every unit that shares it. Moving it to another unit gives it that unit's definition instead.
/// </summary>
public record UpdateAlarmDto(
    string Title,
    string? AlarmCode,
    string Severity,
    string? Description,
    int EquipmentId
);

public record AlarmWriteResult(WriteStatus Status, AlarmDto? Alarm = null, string? Error = null)
{
    public static AlarmWriteResult NotFound() => new(WriteStatus.NotFound);
    public static AlarmWriteResult Invalid(string error) => new(WriteStatus.Invalid, Error: error);
    public static AlarmWriteResult Conflict(string error) => new(WriteStatus.Conflict, Error: error);
}
