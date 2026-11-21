using Larmcentralen.Client.Core.Models;

namespace Larmcentralen.Client.Core.Search;

/// <summary>
/// A search. <see cref="AlarmCode"/>: only alarms with that code (any case), or <see cref="WithoutCode"/> for alarms
/// that have none; null for all.
/// </summary>
public record AlarmSearchQuery(
    string? Text,
    int? AreaId = null,
    int? EquipmentId = null,
    string? Severity = null,
    int Skip = 0,
    int Take = AlarmSearchQuery.PageSize,
    string? AlarmCode = null)
{
    public const int PageSize = 10;

    /// <summary>The code filter value for "Utan larmkod". Never a real code: codes have no spaces.</summary>
    public const string WithoutCode = "utan larmkod";

    /// <summary>True when the alarm passes the code filter.</summary>
    public bool MatchesCode(string? alarmCode)
        => AlarmCode is null
           || (AlarmCode == WithoutCode
               ? string.IsNullOrWhiteSpace(alarmCode)
               : string.Equals(alarmCode?.Trim(), AlarmCode.Trim(), StringComparison.OrdinalIgnoreCase));
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
    private readonly ILookup<int, int> _alarmIdsBySolution;
    private readonly Dictionary<int, Solution> _solutions;
    private readonly Dictionary<int, AlarmCode> _codes;

    public static readonly AlarmIndex Empty = new(new SyncSnapshot(DateTime.MinValue, [], [], [], []));

    public AlarmIndex(SyncSnapshot snapshot)
    {
        _areas = snapshot.Areas.ToDictionary(a => a.Id);
        _equipment = snapshot.Equipment.ToDictionary(e => e.Id);
        _solutions = snapshot.Solutions.ToDictionary(s => s.Id);
        _codes = (snapshot.AlarmCodes ?? []).ToDictionary(c => c.Id);
        SupportsAlarmCodes = snapshot.AlarmCodes is not null;

        // A server without shared solutions sends no links: each solution then belongs to its AlarmId only,
        // and the app hides everything about sharing.
        SupportsSharing = snapshot.AlarmSolutions is not null;
        var links = (snapshot.AlarmSolutions ?? snapshot.Solutions.Select(s => new AlarmSolution(s.AlarmId, s.Id)).ToList())
            .Where(l => _solutions.ContainsKey(l.SolutionId))
            .Distinct()
            .ToList();
        _solutionsByAlarm = links.ToLookup(l => l.AlarmId, l => _solutions[l.SolutionId]);
        _alarmIdsBySolution = links.ToLookup(l => l.SolutionId, l => l.AlarmId);

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
    /// True when the server sends which alarms each solution belongs to. False for a server that
    /// predates shared solutions; then the app shows no sharing features.
    /// </summary>
    public bool SupportsSharing { get; private init; }

    /// <summary>True when the server sends the shared list of alarm codes (then the code is picked from it).</summary>
    public bool SupportsAlarmCodes { get; private init; }

    public AlarmCode? GetCode(int codeId) => _codes.GetValueOrDefault(codeId);

    /// <summary>
    /// The same alarm code on other units of the same type: for M200.0 on T101, M200.0 on T102, T103 and T104 (with their
    /// own text). Units with no type, or "Övrigt", are never related to other units. Alarms without a code have none.
    /// Ordered by unit (natural order).
    /// </summary>
    public IReadOnlyList<AlarmView> SameCode(int alarmId)
    {
        if (!_alarms.TryGetValue(alarmId, out var current)) return [];
        var type = TypeOf(current.View.Equipment);
        var code = NormaliseCode(current.View.Alarm.AlarmCode);
        if (code is null || type is null || type == OtherType) return [];

        return _alarms.Values
            .Select(a => a.View)
            .Where(v => v.Equipment.Id != current.View.Equipment.Id && TypeOf(v.Equipment) == type
                        && NormaliseCode(v.Alarm.AlarmCode) == code)
            .OrderBy(v => v.Equipment.Title, NaturalComparer.Instance)
            .ToList();
    }

    /// <summary>
    /// Alarm codes to pick from for an alarm on <paramref name="equipmentId"/>: each code once, filtered by what is typed
    /// (anywhere in the code, any case), ordered by code (natural order: M2.1 before M10.1). A code the unit already has on
    /// another alarm than <paramref name="editingAlarmId"/> is listed with the reason it cannot be picked.
    /// </summary>
    public IReadOnlyList<CodeOption> SearchCodes(string? text, int equipmentId, int? editingAlarmId = null, int take = 50)
    {
        var term = NormaliseCode(text);
        var used = _alarms.Values.Select(a => a.View.Alarm)
            .Where(a => a.AlarmCodeId is not null)
            .ToLookup(a => a.AlarmCodeId!.Value);

        return _codes.Values
            .Where(c => term is null || c.Code.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => term is not null && !c.Code.StartsWith(term, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(c => c.Code, NaturalComparer.Instance)
            .Take(Math.Max(0, take))
            .Select(c =>
            {
                var alarms = used[c.Id].ToList();
                var taken = alarms.Any(a => a.EquipmentId == equipmentId && a.Id != editingAlarmId);
                return new CodeOption(c, alarms.Select(a => a.EquipmentId).Distinct().Count(),
                    taken ? "Finns redan på den här enheten" : null);
            })
            .ToList();
    }

    /// <summary>The type name units without a type are grouped under, and that gets no suggestions.</summary>
    public const string OtherType = "Övrigt";

    /// <summary>Every alarm a solution belongs to, ordered by unit (natural order: T2 before T10).</summary>
    public IReadOnlyList<AlarmView> AlarmsForSolution(int solutionId)
        => _alarmIdsBySolution[solutionId]
            .Select(id => _alarms.TryGetValue(id, out var a) ? a.View : null)
            .OfType<AlarmView>()
            .OrderBy(v => v.Equipment.Title, NaturalComparer.Instance)
            .ThenBy(v => v.Alarm.Id)
            .ToList();

    /// <summary>
    /// Alarms that are probably the same fault as <paramref name="alarmId"/> on another unit of the same kind:
    /// units with the same <see cref="Models.Equipment.EquipmentType"/>, and the same alarm code or the same
    /// alarm text (ignoring case, spacing and accents). Härdugnar never suggest anlöpningsugnar and so on;
    /// units with no type, or <see cref="OtherType"/>, get no suggestions. A missing code never matches by code.
    /// Ordered with same-text alarms first, then by unit.
    /// </summary>
    public IReadOnlyList<AlarmView> Siblings(int alarmId)
    {
        if (!_alarms.TryGetValue(alarmId, out var current)) return [];
        var type = TypeOf(current.View.Equipment);
        if (type is null || type == OtherType) return [];

        var code = NormaliseCode(current.View.Alarm.AlarmCode);
        var text = NormaliseText(current.View.Alarm.Title);

        return _alarms.Values
            .Where(a => a.View.Alarm.Id != alarmId && TypeOf(a.View.Equipment) == type)
            .Select(a => (View: a.View, SameText: NormaliseText(a.View.Alarm.Title) == text,
                SameCode: code is not null && NormaliseCode(a.View.Alarm.AlarmCode) == code))
            .Where(x => x.SameText || x.SameCode)
            .OrderByDescending(x => x.SameText)
            .ThenBy(x => x.View.Equipment.Title, NaturalComparer.Instance)
            .ThenBy(x => x.View.Alarm.AlarmCode ?? "", NaturalComparer.Instance)
            .Select(x => x.View)
            .ToList();
    }

    /// <summary>
    /// Units grouped by type for lists such as the unit filter: types alphabetically, <see cref="OtherType"/>
    /// last (units without a type go there). One group with a null name when no unit has a type.
    /// </summary>
    public IReadOnlyList<(string? Type, IReadOnlyList<Equipment> Units)> EquipmentByType(int? areaId)
    {
        var units = EquipmentInArea(areaId).ToList();
        if (units.All(e => TypeOf(e) is null)) return [(null, units)];

        return units
            .GroupBy(e => TypeOf(e) ?? OtherType)
            .OrderBy(g => g.Key == OtherType ? 1 : 0)
            .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => ((string?)g.Key, (IReadOnlyList<Equipment>)g.OrderBy(e => e.Title, NaturalComparer.Instance).ToList()))
            .ToList();
    }

    /// <summary>The unit's type, trimmed; null when it has none.</summary>
    public static string? TypeOf(Equipment equipment)
        => string.IsNullOrWhiteSpace(equipment.EquipmentType) ? null : equipment.EquipmentType.Trim();

    private static string? NormaliseCode(string? code)
        => string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();

    /// <summary>Lower case, accents removed (å → a), runs of whitespace collapsed to one space.</summary>
    public static string NormaliseText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var decomposed = text.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(decomposed.Length);
        var space = false;
        foreach (var ch in decomposed)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(ch)) { space = sb.Length > 0; continue; }
            if (space) { sb.Append(' '); space = false; }
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Searches the alarms. Every typed term must match the start of a word in the alarm's
    /// code, title, equipment (technical or everyday name), area or description.
    /// Empty text with no filter returns nothing. Empty text with at least one filter lists every
    /// matching alarm, ordered by alarm code (natural order), alarms without a code last, then title.
    /// </summary>
    /// <summary>
    /// The codes that alarms in this area or on this unit have, each once (upper case), in natural order (M2.1 before
    /// M10.1), for the code filter. <paramref name="withoutCode"/> is true when some alarm there has no code.
    /// </summary>
    public IReadOnlyList<string> CodesIn(int? areaId, int? equipmentId, out bool withoutCode)
    {
        var views = _alarms.Values.Select(a => a.View)
            .Where(v => areaId is not { } area || v.Area.Id == area)
            .Where(v => equipmentId is not { } unit || v.Equipment.Id == unit)
            .ToList();
        withoutCode = views.Any(v => string.IsNullOrWhiteSpace(v.Alarm.AlarmCode));
        return views.Select(v => NormaliseCode(v.Alarm.AlarmCode)).OfType<string>().Distinct()
            .Order(NaturalComparer.Instance).ToList();
    }

    public AlarmSearchResult Search(AlarmSearchQuery query)
    {
        var terms = Tokenize(query.Text);
        if (terms.Count == 0)
        {
            var hasFilter = query.AreaId is not null || query.EquipmentId is not null
                || !string.IsNullOrEmpty(query.Severity) || query.AlarmCode is not null;
            return hasFilter ? ListFiltered(query) : AlarmSearchResult.Empty;
        }

        var matches = new List<(IndexedAlarm Alarm, int Score)>();
        foreach (var alarm in _alarms.Values)
        {
            var view = alarm.View;
            if (query.AreaId is { } areaId && view.Area.Id != areaId) continue;
            if (query.EquipmentId is { } equipmentId && view.Equipment.Id != equipmentId) continue;
            if (!string.IsNullOrEmpty(query.Severity) && view.Alarm.Severity != query.Severity) continue;
            if (!query.MatchesCode(view.Alarm.AlarmCode)) continue;

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
            .Where(v => query.MatchesCode(v.Alarm.AlarmCode))
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

/// <summary>An alarm code offered in the code picker, with how many units use it.</summary>
public sealed record CodeOption(AlarmCode Code, int UnitCount, string? DisabledReason)
{
    public bool Disabled => DisabledReason is not null;
}
