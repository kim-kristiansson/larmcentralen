namespace Larmcentralen.Application.DTOs;

/// <summary>
/// A solution and the alarms it is linked to. A solution can be shared by several alarms
/// (the same fault on several units). <see cref="AlarmId"/> is kept for older clients and is
/// the lowest linked alarm id; new clients read <see cref="AlarmIds"/>.
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
    List<int> AlarmIds
);

/// <summary>
/// New solution. Send <see cref="AlarmIds"/> (one or more alarms). Older clients send a single
/// <see cref="AlarmId"/>, which is used when <see cref="AlarmIds"/> is missing.
/// </summary>
public record CreateSolutionDto(
    string Title,
    string? Content,
    int SortOrder,
    string? EstimatedTime,
    int? AlarmId = null,
    List<int>? AlarmIds = null
);

/// <summary>
/// Changes a solution. <see cref="AlarmIds"/> null leaves the links as they are (older clients);
/// a list replaces the set of linked alarms and must not be empty.
/// </summary>
public record UpdateSolutionDto(
    string Title,
    string? Content,
    int SortOrder,
    string? EstimatedTime,
    List<int>? AlarmIds = null
);

public enum WriteStatus { Ok, NotFound, Invalid, Conflict }

/// <summary>
/// Outcome of a change to a solution. <see cref="AffectedAlarmIds"/> are the alarms whose
/// solutions changed (before and after), for example to refresh their SharePoint documents.
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
