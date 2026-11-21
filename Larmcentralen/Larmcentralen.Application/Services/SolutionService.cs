using Larmcentralen.Application.DTOs;
using Larmcentralen.Application.Interfaces;
using Larmcentralen.Domain.Entities;
using Larmcentralen.Domain.Interfaces;

namespace Larmcentralen.Application.Services;

/// <summary>
/// Solutions and their links to alarm definitions. A solution written for T101 M200.1 shows on every
/// unit that shares that definition, and can be shared with other definitions too. A solution always
/// has at least one definition: the last link cannot be removed, the solution is deleted instead.
/// Calls that name an alarm act on that alarm's definition.
/// </summary>
public class SolutionService(
    ISolutionRepository repo,
    IAlarmRepository alarms,
    IAlarmDefinitionRepository definitions) : ISolutionService
{
    public async Task<List<SolutionDto>> GetByAlarmIdAsync(int alarmId)
        => (await repo.GetByAlarmIdAsync(alarmId)).Select(ToDto).ToList();

    public async Task<SolutionDto?> GetByIdAsync(int id)
    {
        var s = await repo.GetByIdWithDetailsAsync(id);
        return s is null ? null : ToDto(s);
    }

    public async Task<SolutionWriteResult> CreateAsync(CreateSolutionDto dto)
    {
        var alarmIds = dto.AlarmIds ?? (dto.AlarmId is { } single ? [single] : null);
        var (linked, error) = await ResolveDefinitionsAsync(dto.AlarmDefinitionIds, alarmIds);
        if (error is not null) return SolutionWriteResult.Invalid(error);

        var solution = new Solution
        {
            Title = dto.Title,
            Content = dto.Content,
            SortOrder = dto.SortOrder,
            EstimatedTime = dto.EstimatedTime
        };
        foreach (var d in linked!) AlarmDefinitions.Link(d, solution);

        await repo.AddAsync(solution);
        await repo.SaveChangesAsync();

        return new SolutionWriteResult(WriteStatus.Ok, ToDto(solution), AffectedAlarmIds: AlarmIdsOf(solution));
    }

    public async Task<SolutionWriteResult> UpdateAsync(int id, UpdateSolutionDto dto)
    {
        var solution = await repo.GetByIdWithDetailsAsync(id);
        if (solution is null) return SolutionWriteResult.NotFound();

        var before = AlarmIdsOf(solution);

        if (dto.AlarmDefinitionIds is not null || dto.AlarmIds is not null)
        {
            var (linked, error) = await ResolveDefinitionsAsync(dto.AlarmDefinitionIds, dto.AlarmIds);
            if (error is not null) return SolutionWriteResult.Invalid(error);

            // Replace the set: drop links that are gone, add the new ones.
            foreach (var gone in solution.Definitions.Where(d => !linked!.Contains(d)).ToList())
                AlarmDefinitions.Unlink(gone, solution);
            foreach (var d in linked!) AlarmDefinitions.Link(d, solution);
        }

        solution.Title = dto.Title;
        solution.Content = dto.Content;
        solution.SortOrder = dto.SortOrder;
        solution.EstimatedTime = dto.EstimatedTime;
        solution.UpdatedAt = DateTime.UtcNow;

        await repo.SaveChangesAsync();

        return new SolutionWriteResult(WriteStatus.Ok, ToDto(solution),
            AffectedAlarmIds: before.Union(AlarmIdsOf(solution)).Order().ToList());
    }

    /// <summary>Deletes the solution for every definition (and so every unit) it is linked to.</summary>
    public async Task<SolutionWriteResult> DeleteAsync(int id)
    {
        var solution = await repo.GetByIdWithDetailsAsync(id);
        if (solution is null) return SolutionWriteResult.NotFound();

        // Captured before the delete, so the caller can refresh every alarm that showed it.
        var affected = AlarmIdsOf(solution);
        repo.Remove(solution);
        await repo.SaveChangesAsync();
        return new SolutionWriteResult(WriteStatus.Ok, AffectedAlarmIds: affected);
    }

    /// <summary>Removes the solution from the definition of this alarm (all units sharing it). Conflict on the last link.</summary>
    public async Task<SolutionWriteResult> UnlinkAsync(int alarmId, int solutionId)
        => await DefinitionOfAsync(alarmId) is { } definitionId
            ? await UnlinkDefinitionAsync(definitionId, solutionId)
            : SolutionWriteResult.NotFound();

    public async Task<SolutionWriteResult> UnlinkDefinitionAsync(int definitionId, int solutionId)
    {
        var solution = await repo.GetByIdWithDetailsAsync(solutionId);
        var definition = solution?.Definitions.FirstOrDefault(d => d.Id == definitionId);
        if (solution is null || definition is null) return SolutionWriteResult.NotFound();
        if (solution.Definitions.Count == 1)
            return SolutionWriteResult.Conflict("Lösningen gäller bara det här larmet. Ta bort lösningen i stället.");

        var affected = definition.Alarms.Select(a => a.Id).Order().ToList();
        AlarmDefinitions.Unlink(definition, solution);
        solution.UpdatedAt = DateTime.UtcNow;
        await repo.SaveChangesAsync();
        return new SolutionWriteResult(WriteStatus.Ok, ToDto(solution), AffectedAlarmIds: affected);
    }

    /// <summary>Gives the definition of this alarm its own copy of a shared solution.</summary>
    public async Task<SolutionWriteResult> DetachAsync(int alarmId, int solutionId)
        => await DefinitionOfAsync(alarmId) is { } definitionId
            ? await DetachDefinitionAsync(definitionId, solutionId)
            : SolutionWriteResult.NotFound();

    /// <summary>
    /// Gives one definition its own copy of a shared solution: copies it, links the copy to that definition
    /// and removes the shared one from it, in one save (one transaction). The other definitions keep the
    /// original unchanged. A solution that only this definition uses is returned as it is.
    /// </summary>
    public async Task<SolutionWriteResult> DetachDefinitionAsync(int definitionId, int solutionId)
    {
        var solution = await repo.GetByIdWithDetailsAsync(solutionId);
        var definition = solution?.Definitions.FirstOrDefault(d => d.Id == definitionId);
        if (solution is null || definition is null) return SolutionWriteResult.NotFound();
        if (solution.Definitions.Count == 1)
            return new SolutionWriteResult(WriteStatus.Ok, ToDto(solution), AffectedAlarmIds: []);

        var copy = new Solution
        {
            Title = solution.Title,
            Content = solution.Content,
            SortOrder = solution.SortOrder,
            EstimatedTime = solution.EstimatedTime
        };
        AlarmDefinitions.Unlink(definition, solution);
        AlarmDefinitions.Link(definition, copy);
        await repo.AddAsync(copy);
        await repo.SaveChangesAsync();

        return new SolutionWriteResult(WriteStatus.Ok, ToDto(copy),
            AffectedAlarmIds: definition.Alarms.Select(a => a.Id).Order().ToList());
    }

    private async Task<int?> DefinitionOfAsync(int alarmId)
        => (await alarms.GetByIdsAsync([alarmId])).FirstOrDefault()?.AlarmDefinitionId;

    /// <summary>The definitions to link: given directly, or as alarms (each alarm's definition).</summary>
    private async Task<(List<AlarmDefinition>? Definitions, string? Error)> ResolveDefinitionsAsync(
        List<int>? definitionIds, List<int>? alarmIds)
    {
        var ids = new HashSet<int>();
        if (definitionIds is not null)
        {
            var distinct = definitionIds.Distinct().ToList();
            var found = await definitions.GetByIdsAsync(distinct);
            var missing = distinct.Except(found.Select(d => d.Id)).ToList();
            if (missing.Count > 0) return (null, $"Larmdefinitionen finns inte: {string.Join(", ", missing)}.");
            ids.UnionWith(distinct);
        }
        if (alarmIds is not null)
        {
            var distinct = alarmIds.Distinct().ToList();
            var found = await alarms.GetByIdsAsync(distinct);
            var missing = distinct.Except(found.Select(a => a.Id)).ToList();
            if (missing.Count > 0) return (null, $"Larmet finns inte: {string.Join(", ", missing)}.");
            ids.UnionWith(found.Select(a => a.AlarmDefinitionId));
        }
        if (ids.Count == 0) return (null, "Ange minst ett larm som lösningen gäller.");

        // Tracked instances, with their alarms, so links and affected alarms are right before saving.
        var result = new List<AlarmDefinition>();
        foreach (var id in ids.Order())
            result.Add((await definitions.GetByIdWithDetailsAsync(id))!);
        return (result, null);
    }

    private static List<int> AlarmIdsOf(Solution s)
        => s.Definitions.SelectMany(d => d.Alarms).Select(a => a.Id).Distinct().Order().ToList();

    internal static SolutionDto ToDto(Solution s)
    {
        var alarmIds = AlarmIdsOf(s);
        return new SolutionDto(
            s.Id, s.Title, s.Content, s.SortOrder, s.EstimatedTime,
            alarmIds.Count > 0 ? alarmIds[0] : 0, s.CreatedAt, s.UpdatedAt,
            alarmIds, s.Definitions.Select(d => d.Id).Order().ToList());
    }
}
