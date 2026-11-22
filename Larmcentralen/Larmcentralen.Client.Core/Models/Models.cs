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

/// <summary>A unit. <see cref="EquipmentType"/> (e.g. "Härdugn") groups units in lists; older servers don't send it.</summary>
public record Equipment(int Id, string Title, string? DisplayName, int AreaId, DateTime? UpdatedAt, string? EquipmentType = null)
{
    /// <summary>What people call it, falling back to the technical title.</summary>
    public string Name => string.IsNullOrWhiteSpace(DisplayName) ? Title : DisplayName;
}

/// <summary>
/// An alarm on one unit, with its own text. <see cref="AlarmCode"/> is the code as text; <see cref="AlarmCodeId"/> points
/// to the shared code row (null from older servers, or for rows without a code).
/// </summary>
public record Alarm(
    int Id,
    string Title,
    string? AlarmCode,
    string Severity,
    string? Description,
    int EquipmentId,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    int? AlarmCodeId = null);

/// <summary>A PLC alarm code, listed once however many units use it.</summary>
public record AlarmCode(int Id, string Code, string? Description);

/// <summary>
/// A solution. It can be shared by several alarms: the links are in <see cref="SyncSnapshot.AlarmSolutions"/>.
/// <see cref="AlarmId"/> is one of them (the lowest), used only when the server sends no links.
/// </summary>
public record Solution(
    int Id,
    string Title,
    string? Content,
    int SortOrder,
    string? EstimatedTime,
    int AlarmId,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

/// <summary>Links an alarm to a solution.</summary>
public record AlarmSolution(int AlarmId, int SolutionId);

/// <summary>
/// Response of GET /api/sync. <see cref="AlarmSolutions"/> is null from a server that predates shared
/// solutions; then each solution belongs to its <see cref="Solution.AlarmId"/> only.
/// <see cref="AlarmCodes"/> is null from a server without the shared code list.
/// </summary>
public record SyncSnapshot(
    DateTime GeneratedAt,
    List<Area> Areas,
    List<Equipment> Equipment,
    List<Alarm> Alarms,
    List<Solution> Solutions,
    List<AlarmSolution>? AlarmSolutions = null,
    List<AlarmCode>? AlarmCodes = null);

/// <summary>An alarm with its equipment and area resolved, ready to show.</summary>
public record AlarmView(Alarm Alarm, Equipment Equipment, Area Area);

/// <summary>An alarm with everything the detail page needs.</summary>
public record AlarmDetails(Alarm Alarm, Equipment Equipment, Area Area, IReadOnlyList<Solution> Solutions);

// Write contracts, matching the API's Create/Update DTOs.
/// <summary>Create or change an alarm. The code as <see cref="AlarmCodeId"/> (picked from the list), or as text.</summary>
public record SaveAlarmRequest(string Title, string? AlarmCode, string Severity, string? Description, int EquipmentId,
    int? AlarmCodeId = null);

/// <summary><see cref="AlarmId"/> is for servers that predate shared solutions; newer ones use <see cref="AlarmIds"/>.</summary>
public record CreateSolutionRequest(string Title, string? Content, int SortOrder, string? EstimatedTime, int AlarmId, List<int>? AlarmIds = null);

/// <summary><see cref="AlarmIds"/> null leaves the links unchanged; a list replaces them.</summary>
public record UpdateSolutionRequest(string Title, string? Content, int SortOrder, string? EstimatedTime, List<int>? AlarmIds = null);

/// <summary>Create or change an area (admin only).</summary>
public record SaveAreaRequest(string Title, string? DisplayName = null, string? Description = null, string? Location = null);

/// <summary>Create or change a unit (admin only). An empty <see cref="EquipmentType"/> clears the type.</summary>
public record SaveEquipmentRequest(string Title, string? DisplayName, int AreaId, string? EquipmentType,
    string? Category = null, string? Description = null);

/// <summary>What removing an avdelning or unit would take with it (admin only). <see cref="Kind"/> is "Area" or "Equipment".</summary>
public record TrashPreview(string Kind, int ItemId, string Title, int EquipmentCount, int AlarmCount, int SolutionCount);

/// <summary>One removal in the bin (Papperskorg). Erased for good at <see cref="EraseAt"/> unless restored.</summary>
public record TrashItem(int Id, string Kind, int ItemId, string Title, string? AreaTitle,
    int EquipmentCount, int AlarmCount, int SolutionCount, DateTime DeletedAt, DateTime EraseAt)
{
    public bool IsArea => Kind == "Area";
}

public record CreatedEntity(int Id);

/// <summary>Response of GET /api/me: what this app's API key may do. Only admins can edit.</summary>
public record AccessInfo(string Role, bool CanEdit);
