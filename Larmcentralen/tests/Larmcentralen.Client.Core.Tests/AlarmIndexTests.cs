using Larmcentralen.Client.Core.Models;
using Larmcentralen.Client.Core.Search;

namespace Larmcentralen.Client.Core.Tests;

public class AlarmIndexTests
{
    private readonly AlarmIndex _index = new(TestData.Snapshot());

    private List<int> Search(string text, int? areaId = null, int? equipmentId = null, string? severity = null)
        => _index.Search(new AlarmSearchQuery(text, areaId, equipmentId, severity, Take: 100))
            .Items.Select(a => a.Alarm.Id).ToList();

    [Fact]
    public void Empty_text_and_no_filters_returns_nothing()
    {
        Assert.Empty(Search(""));
        Assert.Empty(Search("   "));
        Assert.Equal(0, _index.Search(new AlarmSearchQuery(null)).TotalCount);
        Assert.Empty(Search("", severity: ""));
    }

    [Fact]
    public void Filter_only_by_area_lists_all_alarms_in_the_area()
    {
        // Ordered by code: M200.4, M201.0, M202.1, M206.6, M300.0.
        Assert.Equal([100, 102, 103, 101, 104], Search("", areaId: 1));
        Assert.Equal([105], Search("   ", areaId: 2));
    }

    [Fact]
    public void Filter_only_by_equipment()
    {
        Assert.Equal([100, 101], Search("", equipmentId: 10));
        Assert.Equal([104], Search(null!, equipmentId: 12));
    }

    [Fact]
    public void Filter_only_by_severity()
    {
        Assert.Equal([101, 104, 105], Search("", severity: Severity.Critical));
        Assert.Equal([103], Search("", severity: Severity.Low));
    }

    [Fact]
    public void Filter_only_with_combined_filters()
    {
        Assert.Equal([101], Search("", areaId: 1, equipmentId: 10, severity: Severity.Critical));
        Assert.Equal([104], Search("", areaId: 1, severity: Severity.Critical, equipmentId: 12));
        Assert.Empty(Search("", areaId: 2, severity: Severity.Low));
    }

    [Fact]
    public void Filter_only_sorts_by_code_naturally_with_codeless_last_then_title()
    {
        var created = new DateTime(2026, 3, 20, 9, 0, 0, DateTimeKind.Utc);
        var index = new AlarmIndex(new SyncSnapshot(created,
            [new Area(1, "A", null, null)],
            [new Equipment(10, "E", null, 1, null)],
            [
                new Alarm(1, "Zeta", null, Severity.Low, null, 10, created, null),
                new Alarm(2, "Ten", "M200.10", Severity.Low, null, 10, created, null),
                new Alarm(3, "Two", "M200.1", Severity.Low, null, 10, created, null),
                new Alarm(4, "Hundred", "M200.4", Severity.Low, null, 10, created, null),
                new Alarm(5, "Small", "M2.1", Severity.Low, null, 10, created, null),
                new Alarm(6, "Mid", "M10.1", Severity.Low, null, 10, created, null),
                new Alarm(7, "Alpha", "", Severity.Low, null, 10, created, null),
            ],
            []));

        var ids = index.Search(new AlarmSearchQuery("", AreaId: 1, Take: 100)).Items.Select(a => a.Alarm.Id);
        // M2.1 < M10.1 < M200.1 < M200.4 < M200.10, then codeless by title (Alpha, Zeta).
        Assert.Equal([5, 6, 3, 4, 2, 7, 1], ids);
    }

    [Fact]
    public void Filter_only_pages_like_text_search()
    {
        var first = _index.Search(new AlarmSearchQuery("", AreaId: 1, Take: 2));
        Assert.Equal([100, 102], first.Items.Select(a => a.Alarm.Id));
        Assert.Equal(5, first.TotalCount);
        Assert.True(first.HasMore(first.Items.Count));

        var last = _index.Search(new AlarmSearchQuery("", AreaId: 1, Skip: 4, Take: 2));
        Assert.Single(last.Items);
        Assert.False(last.HasMore(4 + last.Items.Count));

        var beyond = _index.Search(new AlarmSearchQuery("", AreaId: 1, Skip: 50));
        Assert.Empty(beyond.Items);
        Assert.Equal(5, beyond.TotalCount);
    }

    [Fact]
    public void Text_search_with_filters_still_ranks_and_narrows()
    {
        Assert.Equal([100, 103], Search("temperatur", areaId: 1));
        Assert.Equal([103], Search("temperatur", equipmentId: 11));
        Assert.Empty(Search("temperatur", areaId: 2));
    }

    [Fact]
    public void Combined_terms_must_all_match()
    {
        // "w2 temp": equipment W2 and a title word starting with temp.
        Assert.Equal([103], Search("w2 temp"));
    }

    [Fact]
    public void Equipment_and_code_without_its_letter_prefix()
    {
        Assert.Equal([100], Search("t101 200.4"));
        Assert.Equal([100], Search("T101 M200.4"));
    }

    [Fact]
    public void Matching_is_by_word_start_not_substring()
    {
        // "w2 s" finds Säkringsautomater, not "tvättbadets" on the same machine, which only contains an s.
        Assert.Equal([102], Search("w2 s"));
    }

    [Fact]
    public void Everyday_equipment_name_matches()
    {
        Assert.Equal([104], Search("laddvagn nödstopp"));
    }

    [Fact]
    public void Swedish_characters_fold_case()
    {
        Assert.Equal([102], Search("SÄKRING"));
        Assert.Contains(104, Search("NÖDSTOPP"));
    }

    [Fact]
    public void Duplicate_codes_on_different_machines_are_both_found()
    {
        Assert.Equal([104, 105], Search("M300.0").Order().ToList());
    }

    [Fact]
    public void Filters_narrow_a_search()
    {
        Assert.Equal([105], Search("nödstopp", areaId: 2));
        Assert.Equal([104], Search("nödstopp", equipmentId: 12));
        Assert.Equal([100], Search("temperatur", severity: "Hög"));
    }

    [Fact]
    public void Title_starting_with_the_term_ranks_before_a_description_match()
    {
        var ids = Search("temperatur");
        Assert.Equal(2, ids.Count);
        // Both titles start with the term; the more severe alarm comes first.
        Assert.Equal([100, 103], ids);

        // "kontrollera" only appears in a description.
        Assert.Equal([100], Search("kontrollera"));
    }

    [Fact]
    public void Exact_code_ranks_first()
    {
        var first = _index.Search(new AlarmSearchQuery("m206.6")).Items.First();
        Assert.Equal(101, first.Alarm.Id);
    }

    [Fact]
    public void Paging_reports_total_and_more()
    {
        var page = _index.Search(new AlarmSearchQuery("m", Take: 2));
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(6, page.TotalCount);
        Assert.True(page.HasMore(page.Items.Count));

        var last = _index.Search(new AlarmSearchQuery("m", Skip: 4, Take: 2));
        Assert.Equal(2, last.Items.Count);
        Assert.False(last.HasMore(6));
    }

    [Fact]
    public void Alarms_with_missing_equipment_are_skipped()
    {
        Assert.Empty(Search("spökarm"));
        Assert.Null(_index.GetDetails(106));
    }

    [Fact]
    public void Details_list_solutions_in_sort_order()
    {
        var details = _index.GetDetails(100)!;
        Assert.Equal("T101", details.Equipment.Title);
        Assert.Equal("OCSS", details.Area.Title);
        Assert.Equal([1001, 1000], details.Solutions.Select(s => s.Id).ToList());
    }

    [Fact]
    public void GetByIds_keeps_the_given_order_and_drops_unknown_ids()
    {
        Assert.Equal([103, 100], _index.GetByIds([103, 42, 100]).Select(a => a.Alarm.Id).ToList());
    }
}
