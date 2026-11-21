using Larmcentralen.Client.Core.Search;

namespace Larmcentralen.Client.UI.Services;

/// <summary>
/// Remembers the search page's text, filters and how many results are shown, so going back
/// from an alarm lands on the same results instead of an empty page.
/// </summary>
public class SearchState
{
    public string Text { get; set; } = "";
    public int? AreaId { get; set; }
    public int? EquipmentId { get; set; }
    public string? Severity { get; set; }
    public int Shown { get; set; } = AlarmSearchQuery.PageSize;

    public AlarmSearchQuery ToQuery() => new(Text, AreaId, EquipmentId, Severity, 0, Shown);

    public void ResetPaging() => Shown = AlarmSearchQuery.PageSize;
}
