using Larmcentralen.Application.DTOs;
using Larmcentralen.Application.Interfaces;
using Larmcentralen.Domain.Entities;
using Larmcentralen.Domain.Interfaces;

namespace Larmcentralen.Application.Services;

/// <summary>
/// Solutions and their links to alarms. A solution can be shared by several alarms, and always
/// has at least one: the last link cannot be removed, the solution is deleted instead.
/// </summary>
public class SolutionService(ISolutionRepository repo, IAlarmRepository alarms) : ISolutionService
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
        var ids = dto.AlarmIds ?? (dto.AlarmId is { } single ? [single] : null);
        var (linked, error) = await ResolveAlarmsAsync(ids);
        if (error is not null) return SolutionWriteResult.Invalid(error);

        var solution = new Solution
        {
            Title = dto.Title,
            Content = dto.Content,
            SortOrder = dto.SortOrder,
            EstimatedTime = dto.EstimatedTime,
            Alarms = linked!
        };

        await repo.AddAsync(solution);
        await repo.SaveChangesAsync();

        return new SolutionWriteResult(WriteStatus.Ok, ToDto(solution),
            AffectedAlarmIds: AlarmIdsOf(solution));
    }

    public async Task<SolutionWriteResult> UpdateAsync(int id, UpdateSolutionDto dto)
    {
        var solution = await repo.GetByIdWithDetailsAsync(id);
        if (solution is null) return SolutionWriteResult.NotFound();

        var before = AlarmIdsOf(solution);

        if (dto.AlarmIds is not null)
        {
            var (linked, error) = await ResolveAlarmsAsync(dto.AlarmIds);
            if (error is not null) return SolutionWriteResult.Invalid(error);

            // Replace the set: drop links that are gone, add the new ones.
            var wanted = linked!.Select(a => a.Id).ToHashSet();
            solution.Alarms.RemoveAll(a => !wanted.Contains(a.Id));
            foreach (var alarm in linked!.Where(a => solution.Alarms.All(x => x.Id != a.Id)))
                solution.Alarms.Add(alarm);
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

    /// <summary>Deletes the solution for every alarm it is linked to.</summary>
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

    /// <summary>Removes the solution from one alarm only. Refused (Conflict) when it is the last link.</summary>
    public async Task<SolutionWriteResult> UnlinkAsync(int alarmId, int solutionId)
    {
        var solution = await repo.GetByIdWithDetailsAsync(solutionId);
        var link = solution?.Alarms.FirstOrDefault(a => a.Id == alarmId);
        if (solution is null || link is null) return SolutionWriteResult.NotFound();
        if (solution.Alarms.Count == 1)
            return SolutionWriteResult.Conflict("Åtgärden gäller bara det här larmet. Ta bort åtgärden i stället.");

        solution.Alarms.Remove(link);
        solution.UpdatedAt = DateTime.UtcNow;
        await repo.SaveChangesAsync();
        return new SolutionWriteResult(WriteStatus.Ok, ToDto(solution), AffectedAlarmIds: [alarmId]);
    }

    /// <summary>
    /// Gives one alarm its own copy of a shared solution: copies it, links the copy to that alarm
    /// and removes the shared one from it, in one save (one transaction). The other alarms keep the
    /// original unchanged. A solution that only this alarm uses is returned as it is.
    /// </summary>
    public async Task<SolutionWriteResult> DetachAsync(int alarmId, int solutionId)
    {
        var solution = await repo.GetByIdWithDetailsAsync(solutionId);
        var alarm = solution?.Alarms.FirstOrDefault(a => a.Id == alarmId);
        if (solution is null || alarm is null) return SolutionWriteResult.NotFound();
        if (solution.Alarms.Count == 1)
            return new SolutionWriteResult(WriteStatus.Ok, ToDto(solution), AffectedAlarmIds: []);

        var copy = new Solution
        {
            Title = solution.Title,
            Content = solution.Content,
            SortOrder = solution.SortOrder,
            EstimatedTime = solution.EstimatedTime,
            Alarms = [alarm]
        };
        solution.Alarms.Remove(alarm);
        await repo.AddAsync(copy);
        await repo.SaveChangesAsync();

        return new SolutionWriteResult(WriteStatus.Ok, ToDto(copy), AffectedAlarmIds: [alarmId]);
    }

    private async Task<(List<Alarm>? Alarms, string? Error)> ResolveAlarmsAsync(List<int>? ids)
    {
        if (ids is null || ids.Count == 0)
            return (null, "Ange minst ett larm som åtgärden gäller.");

        var distinct = ids.Distinct().ToList();
        var found = await alarms.GetByIdsAsync(distinct);
        var missing = distinct.Except(found.Select(a => a.Id)).ToList();
        return missing.Count > 0
            ? (null, $"Larmet finns inte: {string.Join(", ", missing)}.")
            : (found, null);
    }

    private static List<int> AlarmIdsOf(Solution s) => s.Alarms.Select(a => a.Id).Order().ToList();

    internal static SolutionDto ToDto(Solution s)
    {
        var ids = AlarmIdsOf(s);
        return new SolutionDto(
            s.Id, s.Title, s.Content, s.SortOrder, s.EstimatedTime,
            ids.Count > 0 ? ids[0] : 0, s.CreatedAt, s.UpdatedAt, ids);
    }
}
