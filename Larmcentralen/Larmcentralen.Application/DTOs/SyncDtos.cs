namespace Larmcentralen.Application.DTOs;

/// <summary>
/// The whole knowledge base in one response, for clients that keep a local offline copy.
/// Flat lists keyed by id, so the client can store them as tables without walking a tree.
///
/// Newer clients read <see cref="AlarmDefinitions"/> and <see cref="DefinitionSolutions"/>: what each alarm
/// means and which solutions belong to it. Everything else is kept as before, filled in from the
/// definitions, so older clients keep working: alarms carry their definition's title, code, severity and
/// description, and <see cref="AlarmSolutions"/> and <see cref="SyncSolutionDto.AlarmId"/> are expanded per alarm.
/// </summary>
public record SyncSnapshotDto(
    DateTime GeneratedAt,
    List<SyncAreaDto> Areas,
    List<SyncEquipmentDto> Equipment,
    List<SyncAlarmDto> Alarms,
    List<SyncSolutionDto> Solutions,
    List<SyncAlarmSolutionDto> AlarmSolutions,
    List<SyncAlarmDefinitionDto> AlarmDefinitions,
    List<SyncDefinitionSolutionDto> DefinitionSolutions
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
    int AlarmDefinitionId
);

/// <summary><see cref="AlarmId"/> is the lowest alarm showing it, kept so older clients still show the solution.</summary>
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

/// <summary><see cref="ScopeEquipmentId"/> is 0 when units of the type share it, otherwise the one unit that owns it.</summary>
public record SyncAlarmDefinitionDto(
    int Id,
    string EquipmentType,
    int ScopeEquipmentId,
    string? AlarmCode,
    string Title,
    string Severity,
    string? Description,
    DateTime? UpdatedAt
);

public record SyncDefinitionSolutionDto(int AlarmDefinitionId, int SolutionId);
