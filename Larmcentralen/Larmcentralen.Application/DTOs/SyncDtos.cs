namespace Larmcentralen.Application.DTOs;

/// <summary>
/// The whole knowledge base in one response, for clients that keep a local offline copy.
/// Flat lists keyed by id, so the client can store them as tables without walking a tree.
/// <see cref="AlarmSolutions"/> lists which alarms each solution belongs to (a solution can be
/// shared by several units). Older clients ignore it and use <see cref="SyncSolutionDto.AlarmId"/>.
/// <see cref="AlarmCodes"/> is the list of PLC codes; each alarm points to one with <see cref="SyncAlarmDto.AlarmCodeId"/>
/// and still carries the code as text for older clients.
/// </summary>
public record SyncSnapshotDto(
    DateTime GeneratedAt,
    List<SyncAreaDto> Areas,
    List<SyncEquipmentDto> Equipment,
    List<SyncAlarmDto> Alarms,
    List<SyncSolutionDto> Solutions,
    List<SyncAlarmSolutionDto> AlarmSolutions,
    List<SyncAlarmCodeDto> AlarmCodes
);

public record SyncAreaDto(int Id, string Title, string? DisplayName, DateTime? UpdatedAt);

public record SyncEquipmentDto(int Id, string Title, string? DisplayName, int AreaId, DateTime? UpdatedAt, string? EquipmentType);

public record SyncAlarmDto(
    int Id,
    string Title,
    string? AlarmCode,
    string Severity,
    string? Description,
    int EquipmentId,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    int? AlarmCodeId
);

/// <summary><see cref="AlarmId"/> is the lowest linked alarm, kept so older clients still show the solution.</summary>
public record SyncSolutionDto(
    int Id,
    string Title,
    string? Content,
    int SortOrder,
    string? EstimatedTime,
    int AlarmId,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record SyncAlarmSolutionDto(int AlarmId, int SolutionId);
public record SyncAlarmCodeDto(int Id, string Code, string? Description);
