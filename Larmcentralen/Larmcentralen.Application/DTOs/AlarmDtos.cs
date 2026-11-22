namespace Larmcentralen.Application.DTOs;

/// <summary>An alarm on one unit, with its own text. <see cref="AlarmCode"/> is the code as text, for display.</summary>
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
    int? AlarmCodeId = null
);

public record AlarmListDto(
    int Id,
    string Title,
    string? AlarmCode,
    string Severity,
    string EquipmentTitle,
    string AreaTitle
);

/// <summary>
/// A new alarm on a unit. Give the code as <see cref="AlarmCodeId"/> (picked from the list), or as text in
/// <see cref="AlarmCode"/> (older clients; the code row is found or added). No code is fine for rows without one.
/// </summary>
public record CreateAlarmDto(
    string Title,
    string? AlarmCode,
    string Severity,
    string? Description,
    int EquipmentId,
    int? AlarmCodeId = null
);

/// <summary>Changes an alarm on its unit. The code works as in <see cref="CreateAlarmDto"/>; the text is this alarm's own.</summary>
public record UpdateAlarmDto(
    string Title,
    string? AlarmCode,
    string Severity,
    string? Description,
    int EquipmentId,
    int? AlarmCodeId = null
);

public record AlarmWriteResult(WriteStatus Status, AlarmDto? Alarm = null, string? Error = null)
{
    public static AlarmWriteResult NotFound() => new(WriteStatus.NotFound);
    public static AlarmWriteResult Invalid(string error) => new(WriteStatus.Invalid, Error: error);
    public static AlarmWriteResult Conflict(string error) => new(WriteStatus.Conflict, Error: error);
}

/// <summary>A PLC alarm code and how many alarms (units) use it.</summary>
public record AlarmCodeDto(int Id, string Code, string? Description, int AlarmCount);

public record CreateAlarmCodeDto(string Code, string? Description = null);
