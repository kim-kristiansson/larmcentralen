namespace Larmcentralen.Client.Core.Models;

// Client-side copies of the API contract. Kept separate from the backend on purpose:
// the client depends on the HTTP API, not on the server's assemblies.

public static class Severity
{
    public const string Low = "Låg";
    public const string Medium = "Medel";
    public const string High = "Hög";
    public const string Critical = "Kritisk";

    public static readonly IReadOnlyList<string> All = [Low, Medium, High, Critical];

    /// <summary>Higher is more severe. Unknown values sort as Medel.</summary>
    public static int Rank(string? severity) => severity switch
    {
        Low => 0,
        Medium => 1,
        High => 2,
        Critical => 3,
        _ => 1
    };
}

public record Area(int Id, string Title, string? DisplayName, DateTime? UpdatedAt)
{
    public string Name => string.IsNullOrWhiteSpace(DisplayName) ? Title : DisplayName;
}

public record Equipment(int Id, string Title, string? DisplayName, int AreaId, DateTime? UpdatedAt)
{
    /// <summary>What people call it, falling back to the technical title.</summary>
    public string Name => string.IsNullOrWhiteSpace(DisplayName) ? Title : DisplayName;
}

public record Alarm(
    int Id,
    string Title,
    string? AlarmCode,
    string Severity,
    string? Description,
    int EquipmentId,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record Solution(
    int Id,
    string Title,
    string? Content,
    int SortOrder,
    string? EstimatedTime,
    int AlarmId,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

/// <summary>Response of GET /api/sync.</summary>
public record SyncSnapshot(
    DateTime GeneratedAt,
    List<Area> Areas,
    List<Equipment> Equipment,
    List<Alarm> Alarms,
    List<Solution> Solutions);

/// <summary>An alarm with its equipment and area resolved, ready to show.</summary>
public record AlarmView(Alarm Alarm, Equipment Equipment, Area Area);

/// <summary>An alarm with everything the detail page needs.</summary>
public record AlarmDetails(Alarm Alarm, Equipment Equipment, Area Area, IReadOnlyList<Solution> Solutions);

// Write contracts, matching the API's Create/Update DTOs.
public record SaveAlarmRequest(string Title, string? AlarmCode, string Severity, string? Description, int EquipmentId);

public record CreateSolutionRequest(string Title, string? Content, int SortOrder, string? EstimatedTime, int AlarmId);

public record UpdateSolutionRequest(string Title, string? Content, int SortOrder, string? EstimatedTime);

public record CreatedEntity(int Id);
