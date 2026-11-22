using Larmcentralen.Application.DTOs;
using Larmcentralen.Application.Interfaces;
using Larmcentralen.Domain.Entities;
using Larmcentralen.Domain.Interfaces;

namespace Larmcentralen.Application.Services;

/// <summary>
/// Alarms on units. Each alarm has its own text; its code (when it has one) comes from the shared list of alarm
/// codes, and a unit has at most one alarm per code.
/// </summary>
public class AlarmService(
    IAlarmRepository repo,
    IAlarmCodeRepository codes,
    IEquipmentRepository equipment,
    ISolutionRepository solutions) : IAlarmService
{
    public async Task<List<AlarmListDto>> SearchAsync(string? search, int? equipmentId, int? areaId, string? severity, int skip = 0, int take = 10)
    {
        var alarms = await repo.SearchAsync(search, equipmentId, areaId, severity, skip, take);
        return alarms.Select(ToListDto).ToList();
    }

    public async Task<AlarmDto?> GetByIdAsync(int id)
    {
        var a = await repo.GetByIdWithDetailsAsync(id);
        return a is null ? null : ToDto(a);
    }

    public async Task<AlarmWriteResult> CreateAsync(CreateAlarmDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Title)) return AlarmWriteResult.Invalid("Ange larmtexten.");
        if (await equipment.GetByIdAsync(dto.EquipmentId) is null) return AlarmWriteResult.Invalid("Utrustningen finns inte.");

        var (code, error) = await ResolveCodeAsync(dto.AlarmCodeId, dto.AlarmCode, dto.EquipmentId, exceptAlarmId: null);
        if (error is not null) return error;

        var alarm = new Alarm
        {
            Title = dto.Title.Trim(),
            AlarmCode = code,
            Severity = dto.Severity,
            Description = dto.Description,
            EquipmentId = dto.EquipmentId
        };
        await repo.AddAsync(alarm);
        await repo.SaveChangesAsync();

        return new AlarmWriteResult(WriteStatus.Ok, ToDto((await repo.GetByIdWithDetailsAsync(alarm.Id))!));
    }

    public async Task<AlarmWriteResult> UpdateAsync(int id, UpdateAlarmDto dto)
    {
        var alarm = await repo.GetByIdAsync(id);
        if (alarm is null) return AlarmWriteResult.NotFound();
        if (string.IsNullOrWhiteSpace(dto.Title)) return AlarmWriteResult.Invalid("Ange larmtexten.");
        if (dto.EquipmentId != alarm.EquipmentId && await equipment.GetByIdAsync(dto.EquipmentId) is null)
            return AlarmWriteResult.Invalid("Utrustningen finns inte.");

        var (code, error) = await ResolveCodeAsync(dto.AlarmCodeId, dto.AlarmCode, dto.EquipmentId, exceptAlarmId: id);
        if (error is not null) return error;

        alarm.Title = dto.Title.Trim();
        // Set the key for an existing code and the navigation for one added just now (no key yet).
        if (code is null) { alarm.AlarmCode = null; alarm.AlarmCodeId = null; }
        else if (code.Id == 0) alarm.AlarmCode = code;
        else alarm.AlarmCodeId = code.Id;
        alarm.Severity = dto.Severity;
        alarm.Description = dto.Description;
        alarm.EquipmentId = dto.EquipmentId;
        alarm.UpdatedAt = DateTime.UtcNow;

        await repo.SaveChangesAsync();
        return new AlarmWriteResult(WriteStatus.Ok, ToDto((await repo.GetByIdWithDetailsAsync(id))!));
    }

    /// <summary>
    /// Deletes the alarm, and the solutions that only it had. Solutions shared with other alarms stay for them
    /// (the link row goes by cascade). The alarm code stays in the list.
    /// </summary>
    public async Task<bool> DeleteAsync(int id)
    {
        var alarm = await repo.GetByIdWithDetailsAsync(id);
        if (alarm is null) return false;

        // A solution only this alarm uses goes with it. One also linked to an alarm in the bin stays, so restoring
        // that alarm brings it back with its solution.
        foreach (var solution in alarm.Solutions.ToList())
            if (!await solutions.IsLinkedElsewhereAsync(solution.Id, id))
                solutions.Remove(solution);

        repo.Remove(alarm);
        await repo.SaveChangesAsync();
        return true;
    }

    public async Task<List<AlarmListDto>> GetByIdsAsync(List<int> ids)
    {
        var alarms = await repo.GetByIdsAsync(ids);

        return ids
            .Select(id => alarms.FirstOrDefault(a => a.Id == id))
            .OfType<Alarm>()
            .Select(ToListDto)
            .ToList();
    }

    /// <summary>
    /// The code for an alarm on <paramref name="equipmentId"/>: by id (picked from the list), or by text (found, or
    /// added to the list), or none. A unit may not have two alarms with the same code.
    /// </summary>
    private async Task<(AlarmCode? Code, AlarmWriteResult? Error)> ResolveCodeAsync(int? codeId, string? codeText, int equipmentId, int? exceptAlarmId)
    {
        AlarmCode? code;
        if (codeId is { } id)
        {
            code = await codes.GetByIdAsync(id);
            if (code is null) return (null, AlarmWriteResult.Invalid("Larmkoden finns inte."));
        }
        else if (AlarmText.NormaliseCode(codeText) is { } text)
        {
            if (text.Length > 50) return (null, AlarmWriteResult.Invalid("Larmkoden får vara högst 50 tecken."));
            code = await codes.FindAsync(text);
            if (code is null)
            {
                code = new AlarmCode { Code = text };
                await codes.AddAsync(code);
            }
        }
        else
        {
            return (null, null);
        }

        if (code.Id != 0 && await repo.ExistsAsync(code.Id, equipmentId, exceptAlarmId))
            return (null, AlarmWriteResult.Conflict($"Larmkoden {code.Code} finns redan på den här enheten."));
        return (code, null);
    }

    private static AlarmListDto ToListDto(Alarm a) => new(
        a.Id, a.Title, a.AlarmCode?.Code, a.Severity, a.Equipment.Title, a.Equipment.Area.Title);

    private static AlarmDto ToDto(Alarm a) => new(
        a.Id, a.Title, a.AlarmCode?.Code, a.Severity, a.Description,
        a.EquipmentId, a.Equipment.Title, a.Equipment.Area.Title,
        a.CreatedAt, a.UpdatedAt,
        a.Solutions.OrderBy(s => s.SortOrder).ThenBy(s => s.Id).Select(SolutionService.ToDto).ToList(),
        a.AlarmCodeId);
}
