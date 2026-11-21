using Larmcentralen.Client.Core.Models;

namespace Larmcentralen.Client.Core.Search;

public record AlarmSearchQuery(
    string? Text,
    int? AreaId = null,
    int? EquipmentId = null,
    string? Severity = null,
    int Skip = 0,
    int Take = AlarmSearchQuery.PageSize)
{
    public const int PageSize = 10;
}

public record AlarmSearchResult(IReadOnlyList<AlarmView> Items, int TotalCount)
{
    public static readonly AlarmSearchResult Empty = new([], 0);
    public bool HasMore(int shown) => shown < TotalCount;
}

/// <summary>
/// An immutable in-memory index over the local copy. Search runs here, not in SQLite:
/// a few hundred alarms search in well under a millisecond, and .NET's case folding
/// handles å, ä and ö correctly where SQLite's LIKE only folds ASCII.
/// </summary>
public sealed class AlarmIndex
{
    private readonly Dictionary<int, Area> _areas;
    private readonly Dictionary<int, Equipment> _equipment;
    private readonly Dictionary<int, IndexedAlarm> _alarms;
    private readonly ILookup<int, Solution> _solutionsByAlarm;
    private readonly Dictionary<int, Solution> _solutions;

    public static readonly AlarmIndex Empty = new(new SyncSnapshot(DateTime.MinValue, [], [], [], []));

    public AlarmIndex(SyncSnapshot snapshot)
    {
        _areas = snapshot.Areas.ToDictionary(a => a.Id);
        _equipment = snapshot.Equipment.ToDictionary(e => e.Id);
        _solutionsByAlarm = snapshot.Solutions.ToLookup(s => s.AlarmId);
        _solutions = snapshot.Solutions.ToDictionary(s => s.Id);

        _alarms = new Dictionary<int, IndexedAlarm>(snapshot.Alarms.Count);
        foreach (var alarm in snapshot.Alarms)
        {
            // An alarm whose equipment or area is missing is skipped rather than shown half-empty.
            if (!_equipment.TryGetValue(alarm.EquipmentId, out var equipment)) continue;
            if (!_areas.TryGetValue(equipment.AreaId, out var area)) continue;
            _alarms[alarm.Id] = new IndexedAlarm(new AlarmView(alarm, equipment, area));
        }

        Areas = snapshot.Areas.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        Equipment = snapshot.Equipment.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public IReadOnlyList<Area> Areas { get; }
    public IReadOnlyList<Equipment> Equipment { get; }
    public int AlarmCount => _alarms.Count;

    public IEnumerable<Equipment> EquipmentInArea(int? areaId)
        => areaId is null ? Equipment : Equipment.Where(e => e.AreaId == areaId);

    public AlarmDetails? GetDetails(int alarmId)
    {
        if (!_alarms.TryGetValue(alarmId, out var indexed)) return null;
        var view = indexed.View;
        var solutions = _solutionsByAlarm[alarmId].OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToList();
        return new AlarmDetails(view.Alarm, view.Equipment, view.Area, solutions);
    }

    public Solution? GetSolution(int solutionId)
        => _solutions.GetValueOrDefault(solutionId);

    public IReadOnlyList<AlarmView> GetByIds(IEnumerable<int> ids)
        => ids.Select(id => _alarms.TryGetValue(id, out var a) ? a.View : null).OfType<AlarmView>().ToList();

    /// <summary>
    /// Searches the alarms. Every typed term must match the start of a word in the alarm's
    /// code, title, equipment (technical or everyday name), area or description.
    /// Empty text with no filter returns nothing. Empty text with at least one filter lists every
    /// matching alarm, ordered by alarm code (natural order), alarms without a code last, then title.
    /// </summary>
    public AlarmSearchResult Search(AlarmSearchQuery query)
    {
        var terms = Tokenize(query.Text);
        if (terms.Count == 0)
        {
            var hasFilter = query.AreaId is not null || query.EquipmentId is not null
                || !string.IsNullOrEmpty(query.Severity);
            return hasFilter ? ListFiltered(query) : AlarmSearchResult.Empty;
        }

        var matches = new List<(IndexedAlarm Alarm, int Score)>();
        foreach (var alarm in _alarms.Values)
        {
            var view = alarm.View;
            if (query.AreaId is { } areaId && view.Area.Id != areaId) continue;
            if (query.EquipmentId is { } equipmentId && view.Equipment.Id != equipmentId) continue;
            if (!string.IsNullOrEmpty(query.Severity) && view.Alarm.Severity != query.Severity) continue;

            var total = 0;
            var all = true;
            foreach (var term in terms)
            {
                var score = alarm.Score(term);
                if (score == 0) { all = false; break; }
                total += score;
            }
            if (all) matches.Add((alarm, total));
        }

        var ordered = matches
            .OrderByDescending(m => m.Score)
            .ThenByDescending(m => Severity.Rank(m.Alarm.View.Alarm.Severity))
            .ThenBy(m => m.Alarm.View.Alarm.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(m => m.Alarm.View.Alarm.Id)
            .Skip(Math.Max(0, query.Skip))
            .Take(Math.Max(0, query.Take))
            .Select(m => m.Alarm.View)
            .ToList();

        return new AlarmSearchResult(ordered, matches.Count);
    }

    private AlarmSearchResult ListFiltered(AlarmSearchQuery query)
    {
        var matches = _alarms.Values
            .Select(a => a.View)
            .Where(v => query.AreaId is not { } areaId || v.Area.Id == areaId)
            .Where(v => query.EquipmentId is not { } equipmentId || v.Equipment.Id == equipmentId)
            .Where(v => string.IsNullOrEmpty(query.Severity) || v.Alarm.Severity == query.Severity)
            .OrderBy(v => string.IsNullOrWhiteSpace(v.Alarm.AlarmCode) ? 1 : 0)
            .ThenBy(v => v.Alarm.AlarmCode ?? "", NaturalComparer.Instance)
            .ThenBy(v => v.Alarm.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(v => v.Alarm.Id)
            .ToList();

        var page = matches.Skip(Math.Max(0, query.Skip)).Take(Math.Max(0, query.Take)).ToList();
        return new AlarmSearchResult(page, matches.Count);
    }

    /// <summary>Compares strings so that digit runs are ordered by numeric value (M2.1 &lt; M10.1).</summary>
    private sealed class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            x ??= ""; y ??= "";
            int i = 0, j = 0;
            while (i < x.Length && j < y.Length)
            {
                if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
                {
                    var si = i; var sj = j;
                    while (i < x.Length && char.IsDigit(x[i])) i++;
                    while (j < y.Length && char.IsDigit(y[j])) j++;
                    var a = x.AsSpan(si, i - si).TrimStart('0');
                    var b = y.AsSpan(sj, j - sj).TrimStart('0');
                    if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
                    var c = a.CompareTo(b, StringComparison.Ordinal);
                    if (c != 0) return c;
                    // Equal value: fewer leading zeros first, to keep the order deterministic.
                    var lead = (i - si).CompareTo(j - sj);
                    if (lead != 0) return lead;
                }
                else
                {
                    var c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                    if (c != 0) return c;
                    i++; j++;
                }
            }
            return (x.Length - i).CompareTo(y.Length - j);
        }
    }

    internal static List<string> Tokenize(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Fold)
                .Distinct()
                .ToList();

    internal static string Fold(string s) => s.ToLowerInvariant();

    private sealed class IndexedAlarm
    {
        private readonly string _code;
        private readonly string _title;
        private readonly string _equipmentTitle;
        private readonly string _equipmentDisplayName;
        private readonly string _area;
        private readonly string _description;

        public IndexedAlarm(AlarmView view)
        {
            View = view;
            _code = Fold(view.Alarm.AlarmCode ?? "");
            _title = Fold(view.Alarm.Title);
            _equipmentTitle = Fold(view.Equipment.Title);
            _equipmentDisplayName = Fold(view.Equipment.DisplayName ?? "");
            _area = Fold($"{view.Area.Title} {view.Area.DisplayName}");
            _description = Fold(view.Alarm.Description ?? "");
        }

        public AlarmView View { get; }

        /// <summary>How well one term matches. 0 means no match. Higher ranks first.</summary>
        public int Score(string term)
        {
            // Alarm codes are matched anywhere, so "200.4" finds M200.4.
            if (_code.Length > 0)
            {
                if (_code == term) return 100;
                if (_code.StartsWith(term, StringComparison.Ordinal)) return 80;
                if (_code.Contains(term, StringComparison.Ordinal)) return 60;
            }

            if (_title.StartsWith(term, StringComparison.Ordinal)) return 50;
            if (_equipmentTitle == term || _equipmentDisplayName == term) return 45;
            if (StartsWord(_equipmentTitle, term) || StartsWord(_equipmentDisplayName, term)) return 40;
            if (StartsWord(_title, term)) return 30;
            if (StartsWord(_area, term)) return 20;
            if (StartsWord(_description, term)) return 10;
            return 0;
        }

        /// <summary>True when term starts the text or starts a word in it (after a space, dash, slash and so on).</summary>
        internal static bool StartsWord(string text, string term)
        {
            if (text.Length == 0) return false;
            var index = text.IndexOf(term, StringComparison.Ordinal);
            while (index >= 0)
            {
                if (index == 0 || !char.IsLetterOrDigit(text[index - 1])) return true;
                index = text.IndexOf(term, index + 1, StringComparison.Ordinal);
            }
            return false;
        }
    }
}
