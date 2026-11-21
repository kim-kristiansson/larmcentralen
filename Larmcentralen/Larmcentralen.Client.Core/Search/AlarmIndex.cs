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
    private readonly ILookup<int, int> _alarmIdsBySolution;
    private readonly Dictionary<int, Solution> _solutions;
    private readonly Dictionary<int, AlarmDefinition> _definitions;
    private readonly ILookup<int, int> _alarmIdsByDefinition;
    private readonly ILookup<int, int> _definitionIdsBySolution;

    public static readonly AlarmIndex Empty = new(new SyncSnapshot(DateTime.MinValue, [], [], [], []));

    public AlarmIndex(SyncSnapshot snapshot)
    {
        _areas = snapshot.Areas.ToDictionary(a => a.Id);
        _equipment = snapshot.Equipment.ToDictionary(e => e.Id);
        _solutions = snapshot.Solutions.ToDictionary(s => s.Id);

        // Solutions belong to alarm definitions; every alarm of a definition shows them. A server without
        // definitions sends per-alarm links instead, and one without shared solutions only Solution.AlarmId:
        // then the app hides definitions, or all sharing.
        SupportsDefinitions = snapshot.AlarmDefinitions is not null && snapshot.DefinitionSolutions is not null;
        SupportsSharing = SupportsDefinitions || snapshot.AlarmSolutions is not null;
        _definitions = SupportsDefinitions ? snapshot.AlarmDefinitions!.ToDictionary(d => d.Id) : [];
        _alarmIdsByDefinition = snapshot.Alarms.Where(a => SupportsDefinitions && a.AlarmDefinitionId is not null)
            .ToLookup(a => a.AlarmDefinitionId!.Value, a => a.Id);
        _definitionIdsBySolution = (snapshot.DefinitionSolutions ?? []).Where(l => SupportsDefinitions)
            .ToLookup(l => l.SolutionId, l => l.AlarmDefinitionId);
        var alarmLinks = SupportsDefinitions
            ? snapshot.DefinitionSolutions!.SelectMany(l => _alarmIdsByDefinition[l.AlarmDefinitionId].Select(a => new AlarmSolution(a, l.SolutionId))).ToList()
            : snapshot.AlarmSolutions;
        var links = (alarmLinks ?? snapshot.Solutions.Select(s => new AlarmSolution(s.AlarmId, s.Id)).ToList())
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

    /// <summary>True when the server sends alarm definitions (units of a type sharing what an alarm means and its solutions).</summary>
    public bool SupportsDefinitions { get; private init; }

    public AlarmDefinition? DefinitionOf(int alarmId)
        => _alarms.TryGetValue(alarmId, out var a) && a.View.Alarm.AlarmDefinitionId is { } id ? _definitions.GetValueOrDefault(id) : null;

    /// <summary>The same alarm on other units: those sharing its definition, ordered by unit.</summary>
    public IReadOnlyList<AlarmView> SameDefinition(int alarmId)
        => TargetFor(alarmId)?.Alarms.Where(a => a.Alarm.Id != alarmId).ToList() ?? [];

    /// <summary>
    /// What a solution is attached to for this alarm: its definition with all its alarms, or (from a server
    /// without definitions) the alarm alone.
    /// </summary>
    public SolutionTarget? TargetFor(int alarmId)
    {
        if (!_alarms.TryGetValue(alarmId, out var a)) return null;
        if (SupportsDefinitions && a.View.Alarm.AlarmDefinitionId is { } id)
            return new SolutionTarget(id, id, ByUnit(_alarmIdsByDefinition[id]));
        return new SolutionTarget(-alarmId, null, [a.View]);
    }

    /// <summary>Where a solution is attached: one target per definition (or per alarm from older servers).</summary>
    public IReadOnlyList<SolutionTarget> TargetsForSolution(int solutionId)
        => SupportsDefinitions
            ? _definitionIdsBySolution[solutionId].Select(id => new SolutionTarget(id, id, ByUnit(_alarmIdsByDefinition[id])))
                .Where(t => t.Alarms.Count > 0).OrderBy(t => t.Alarms[0].Equipment.Title, NaturalComparer.Instance).ToList()
            : AlarmsForSolution(solutionId).Select(a => new SolutionTarget(-a.Alarm.Id, null, [a])).ToList();

    /// <summary>
    /// <see cref="Siblings"/> as solution targets: other definitions (or alarms) that are probably the same fault,
    /// not counting the alarms that already share this alarm's definition.
    /// </summary>
    public IReadOnlyList<SolutionTarget> SiblingTargets(int alarmId)
    {
        var own = TargetFor(alarmId);
        if (own is null) return [];
        return Siblings(alarmId)
            .Select(a => TargetFor(a.Alarm.Id)!)
            .Where(t => t.Key != own.Key)
            .DistinctBy(t => t.Key)
            .ToList();
    }

    /// <summary>
    /// Alarm definitions to pick an alarm code from, for an alarm on <paramref name="equipmentId"/>. By default only
    /// definitions of that unit's type (<paramref name="allTypes"/> widens it). Every word typed must appear in the
    /// code or the text, ignoring case and accents. A definition the unit cannot use is listed with the reason:
    /// owned by another unit, or the unit already has an alarm with it (other than <paramref name="editingAlarmId"/>).
    /// Ordered by code (natural order), then text.
    /// </summary>
    public IReadOnlyList<DefinitionOption> SearchDefinitions(string? text, int equipmentId, bool allTypes = false,
        int? editingAlarmId = null, int take = 50)
    {
        if (!_equipment.TryGetValue(equipmentId, out var unit)) return [];
        var type = TypeOf(unit) ?? OtherType;
        var terms = string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(SearchKey).ToList();

        return _definitions.Values
            .Where(d => allTypes || d.EquipmentType == type)
            .Where(d =>
            {
                if (terms.Count == 0) return true;
                var key = SearchKey($"{d.AlarmCode} {d.Title}");
                return terms.All(t => key.Contains(t, StringComparison.Ordinal));
            })
            .OrderBy(d => d.AlarmCode ?? "￿", NaturalComparer.Instance)
            .ThenBy(d => d.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(d => d.Id)
            .Take(Math.Max(0, take))
            .Select(d =>
            {
                var alarms = ByUnit(_alarmIdsByDefinition[d.Id]);
                string? reason = null;
                if (d.ScopeEquipmentId != 0 && d.ScopeEquipmentId != equipmentId)
                    reason = $"Gäller bara {(_equipment.TryGetValue(d.ScopeEquipmentId, out var owner) ? owner.Title : "en annan enhet")}";
                else if (alarms.Any(a => a.Equipment.Id == equipmentId && a.Alarm.Id != editingAlarmId))
                    reason = $"Finns redan på {unit.Title}";
                return new DefinitionOption(d, alarms, reason);
            })
            .ToList();
    }

    public AlarmDefinition? GetDefinition(int definitionId) => _definitions.GetValueOrDefault(definitionId);

    /// <summary>The alarms (units) that use a definition, ordered by unit.</summary>
    public IReadOnlyList<AlarmView> AlarmsForDefinition(int definitionId) => ByUnit(_alarmIdsByDefinition[definitionId]);

    /// <summary>Lower case without accents, punctuation kept, so "200.1" and "temperatur elskap" both match.</summary>
    private static string SearchKey(string s)
    {
        var decomposed = s.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }

    private IReadOnlyList<AlarmView> ByUnit(IEnumerable<int> alarmIds)
        => alarmIds.Select(id => _alarms.TryGetValue(id, out var a) ? a.View : null).OfType<AlarmView>()
            .OrderBy(v => v.Equipment.Title, NaturalComparer.Instance).ThenBy(v => v.Alarm.Id).ToList();

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

    /// <summary>
    /// Lower case, accents removed (å → a), whitespace and punctuation removed. The same rule the server uses
    /// to decide which alarms share a definition.
    /// </summary>
    public static string NormaliseText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var decomposed = text.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }

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

/// <summary>
/// Where a solution is attached: an alarm definition with every alarm (unit) that shares it, or, from a server
/// without definitions, one alarm. <see cref="Key"/> is the definition id, or minus the alarm id.
/// </summary>
public sealed record SolutionTarget(int Key, int? DefinitionId, IReadOnlyList<AlarmView> Alarms)
{
    public AlarmView First => Alarms[0];
    public IEnumerable<string> Units => Alarms.Select(a => a.Equipment.Title).Distinct();
}

/// <summary>An alarm definition offered in the alarm code picker, with the units that use it.</summary>
public sealed record DefinitionOption(AlarmDefinition Definition, IReadOnlyList<AlarmView> Alarms, string? DisabledReason)
{
    public bool Disabled => DisabledReason is not null;
    public IEnumerable<string> Units => Alarms.Select(a => a.Equipment.Title).Distinct();
}
