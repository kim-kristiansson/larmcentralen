using Larmcentralen.Domain.Entities;
using Larmcentralen.Domain.Interfaces;

namespace Larmcentralen.Application.Services;

/// <summary>
/// The rules for alarm definitions, in one place for the services and the import tool:
/// alarms on units of the same type with the same code and text share a definition
/// ("Övrigt" units each keep their own), and a definition with no alarms left is deleted,
/// together with the solutions only it had.
/// </summary>
public static class AlarmDefinitions
{
    /// <summary>
    /// The definition this unit's alarm with this code and text belongs to: an existing one with the same
    /// key, or a new (unsaved) one. Severity and description only fill a new definition.
    /// </summary>
    public static async Task<AlarmDefinition> ResolveAsync(IAlarmDefinitionRepository definitions, Equipment equipment,
        string? alarmCode, string title, string severity, string? description)
    {
        var type = AlarmDefinition.TypeFor(equipment);
        var code = AlarmDefinition.NormaliseCode(alarmCode);
        var key = AlarmDefinition.NormaliseTitle(title);
        var scope = AlarmDefinition.DefaultScopeFor(equipment);

        var existing = await definitions.FindAsync(type, code, key, scope);
        if (existing is not null) return existing;

        var created = new AlarmDefinition
        {
            EquipmentType = type,
            ScopeEquipmentId = scope,
            AlarmCode = code,
            Title = title.Trim(),
            NormalizedTitle = key,
            Severity = severity,
            Description = description
        };
        await definitions.AddAsync(created);
        return created;
    }

    /// <summary>Links a solution to a definition, both sides at once, so checks before saving see it.</summary>
    public static void Link(AlarmDefinition definition, Solution solution)
    {
        if (!definition.Solutions.Contains(solution)) definition.Solutions.Add(solution);
        if (!solution.Definitions.Contains(definition)) solution.Definitions.Add(definition);
    }

    /// <summary>Removes the link between a solution and a definition, both sides at once.</summary>
    public static void Unlink(AlarmDefinition definition, Solution solution)
    {
        definition.Solutions.Remove(solution);
        solution.Definitions.Remove(definition);
    }

    /// <summary>
    /// Marks definitions that no alarm uses any more for deletion, and the solutions that only they had.
    /// Needs each definition's Alarms and Solutions (with their Definitions) loaded. Call before saving.
    /// </summary>
    public static void RemoveUnused(IEnumerable<AlarmDefinition> candidates, IAlarmDefinitionRepository definitions,
        ISolutionRepository solutions)
    {
        var unused = candidates.Where(d => d.Alarms.Count == 0).Distinct().ToList();
        foreach (var solution in unused.SelectMany(d => d.Solutions).Distinct().ToList())
        {
            if (solution.Definitions.All(unused.Contains))
                solutions.Remove(solution);
        }
        foreach (var definition in unused) definitions.Remove(definition);
    }
}
