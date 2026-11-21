namespace Larmcentralen.Application.DTOs;

/// <summary>
/// A solution and where it applies. Solutions belong to alarm definitions (<see cref="AlarmDefinitionIds"/>);
/// <see cref="AlarmIds"/> lists every alarm on every unit that shows it. <see cref="AlarmId"/> is kept for
/// older clients and is the lowest of those.
/// </summary>
public record SolutionDto(
    int Id,
    string Title,
    string? Content,
    int SortOrder,
    string? EstimatedTime,
    int AlarmId,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    List<int> AlarmIds,
    List<int> AlarmDefinitionIds
);

/// <summary>
/// New solution. Say where it applies with <see cref="AlarmDefinitionIds"/>, or with <see cref="AlarmIds"/>
/// (each alarm's definition is used), or, from older clients, a single <see cref="AlarmId"/>.
/// </summary>
public record CreateSolutionDto(
    string Title,
    string? Content,
    int SortOrder,
    string? EstimatedTime,
    int? AlarmId = null,
    List<int>? AlarmIds = null,
    List<int>? AlarmDefinitionIds = null
);

/// <summary>
/// Changes a solution. With neither <see cref="AlarmDefinitionIds"/> nor <see cref="AlarmIds"/> the links stay
/// as they are (older clients). Otherwise the set of definitions is replaced and must not be empty.
/// </summary>
public record UpdateSolutionDto(
    string Title,
    string? Content,
    int SortOrder,
    string? EstimatedTime,
    List<int>? AlarmIds = null,
    List<int>? AlarmDefinitionIds = null
);

public enum WriteStatus { Ok, NotFound, Invalid, Conflict }

/// <summary>
/// Outcome of a change to a solution. <see cref="AffectedAlarmIds"/> are the alarms whose solutions
/// changed (before and after), for example to refresh their SharePoint documents.
/// </summary>
public record SolutionWriteResult(
    WriteStatus Status,
    SolutionDto? Solution = null,
    string? Error = null,
    IReadOnlyList<int>? AffectedAlarmIds = null)
{
    public static SolutionWriteResult NotFound() => new(WriteStatus.NotFound);
    public static SolutionWriteResult Invalid(string error) => new(WriteStatus.Invalid, Error: error);
    public static SolutionWriteResult Conflict(string error) => new(WriteStatus.Conflict, Error: error);
}
