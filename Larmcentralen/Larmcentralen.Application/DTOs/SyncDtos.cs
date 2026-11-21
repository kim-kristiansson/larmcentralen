namespace Larmcentralen.Application.DTOs;

/// <summary>
/// The whole knowledge base in one response, for clients that keep a local offline copy.
/// Flat lists keyed by id, so the client can store them as tables without walking a tree.
/// </summary>
public record SyncSnapshotDto(
    DateTime GeneratedAt,
    List<SyncAreaDto> Areas,
    List<SyncEquipmentDto> Equipment,
    List<SyncAlarmDto> Alarms,
    List<SyncSolutionDto> Solutions
);

public record SyncAreaDto(int Id, string Title, string? DisplayName, DateTime? UpdatedAt);

public record SyncEquipmentDto(int Id, string Title, string? DisplayName, int AreaId, DateTime? UpdatedAt);

public record SyncAlarmDto(
    int Id,
    string Title,
    string? AlarmCode,
    string Severity,
    string? Description,
    int EquipmentId,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

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
