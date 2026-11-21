using Larmcentralen.Application.DTOs;
using Larmcentralen.Application.Interfaces;
using Larmcentralen.Domain.Entities;
using Larmcentralen.Domain.Interfaces;

namespace Larmcentralen.Application.Services;

public class AlarmService(
    IAlarmRepository repo,
    IAlarmDefinitionRepository definitions,
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
        var unit = await equipment.GetByIdAsync(dto.EquipmentId);
        if (unit is null) return AlarmWriteResult.Invalid("Utrustningen finns inte.");
        if (string.IsNullOrWhiteSpace(dto.Title)) return AlarmWriteResult.Invalid("Ange larmtexten.");

        var definition = await AlarmDefinitions.ResolveAsync(definitions, unit, dto.AlarmCode, dto.Title, dto.Severity, dto.Description);
        if (definition.Id != 0 && await repo.ExistsAsync(definition.Id, unit.Id))
            return AlarmWriteResult.Conflict("Larmet finns redan på den här enheten.");

        var alarm = new Alarm { EquipmentId = unit.Id, Definition = definition };
        await repo.AddAsync(alarm);
        await repo.SaveChangesAsync();

        return new AlarmWriteResult(WriteStatus.Ok, ToDto((await repo.GetByIdWithDetailsAsync(alarm.Id))!));
    }

    public async Task<AlarmWriteResult> UpdateAsync(int id, UpdateAlarmDto dto)
    {
        var alarm = await repo.GetForUpdateAsync(id);
        if (alarm is null) return AlarmWriteResult.NotFound();
        if (string.IsNullOrWhiteSpace(dto.Title)) return AlarmWriteResult.Invalid("Ange larmtexten.");

        var old = alarm.Definition;

        if (dto.EquipmentId != alarm.EquipmentId)
        {
            // Moving to another unit: the alarm takes that unit's definition for this code and text.
            var unit = await equipment.GetByIdAsync(dto.EquipmentId);
            if (unit is null) return AlarmWriteResult.Invalid("Utrustningen finns inte.");
            var target = await AlarmDefinitions.ResolveAsync(definitions, unit, dto.AlarmCode, dto.Title, dto.Severity, dto.Description);
            if (target.Id != 0 && await repo.ExistsAsync(target.Id, unit.Id))
                return AlarmWriteResult.Conflict("Larmet finns redan på den enheten.");

            // It keeps the solutions it had.
            foreach (var s in old.Solutions.ToList()) AlarmDefinitions.Link(target, s);
            old.Alarms.Remove(alarm);
            alarm.EquipmentId = unit.Id;
            alarm.Definition = target;
            alarm.UpdatedAt = DateTime.UtcNow;
            AlarmDefinitions.RemoveUnused([old], definitions, solutions);
        }
        else
        {
            // The text belongs to the definition: the change applies to every unit that shares it.
            var code = AlarmDefinition.NormaliseCode(dto.AlarmCode);
            var key = AlarmDefinition.NormaliseTitle(dto.Title);
            if (code != old.AlarmCode || key != old.NormalizedTitle)
            {
                var clash = await definitions.FindAsync(old.EquipmentType, code, key, old.ScopeEquipmentId);
                if (clash is not null && clash != old)
                    return AlarmWriteResult.Conflict(
                        $"Det finns redan ett larm {code} ”{clash.Title}” för {old.EquipmentType}. Ändra koden eller texten.");
            }
            old.AlarmCode = code;
            old.Title = dto.Title.Trim();
            old.NormalizedTitle = key;
            old.Severity = dto.Severity;
            old.Description = dto.Description;
            old.UpdatedAt = DateTime.UtcNow;
        }

        await repo.SaveChangesAsync();
        return new AlarmWriteResult(WriteStatus.Ok, ToDto((await repo.GetByIdWithDetailsAsync(id))!));
    }

    /// <summary>
    /// Deletes the alarm on this unit. Its definition goes too when no other unit uses it,
    /// with the solutions only that definition had; shared ones stay for the other units.
    /// </summary>
    public async Task<bool> DeleteAsync(int id)
    {
        var alarm = await repo.GetForUpdateAsync(id);
        if (alarm is null) return false;

        var definition = alarm.Definition;
        definition.Alarms.Remove(alarm);
        repo.Remove(alarm);
        AlarmDefinitions.RemoveUnused([definition], definitions, solutions);
        await repo.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Gives this unit its own definition, a copy of the shared one with the same solutions, so its text can
    /// be changed without changing the other units. A definition only this unit uses is returned as it is.
    /// </summary>
    public async Task<AlarmWriteResult> SplitAsync(int id)
    {
        var alarm = await repo.GetForUpdateAsync(id);
        if (alarm is null) return AlarmWriteResult.NotFound();

        var shared = alarm.Definition;
        if (shared.Alarms.Count > 1)
        {
            var own = await definitions.FindAsync(shared.EquipmentType, shared.AlarmCode, shared.NormalizedTitle, alarm.EquipmentId);
            if (own is null)
            {
                own = new AlarmDefinition
                {
                    EquipmentType = shared.EquipmentType,
                    ScopeEquipmentId = alarm.EquipmentId,
                    AlarmCode = shared.AlarmCode,
                    Title = shared.Title,
                    NormalizedTitle = shared.NormalizedTitle,
                    Severity = shared.Severity,
                    Description = shared.Description
                };
                await definitions.AddAsync(own);
            }
            foreach (var s in shared.Solutions.ToList()) AlarmDefinitions.Link(own, s);
            shared.Alarms.Remove(alarm);
            alarm.Definition = own;
            alarm.UpdatedAt = DateTime.UtcNow;
            await repo.SaveChangesAsync();
        }

        return new AlarmWriteResult(WriteStatus.Ok, ToDto((await repo.GetByIdWithDetailsAsync(id))!));
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

    private static AlarmListDto ToListDto(Alarm a) => new(
        a.Id, a.Definition.Title, a.Definition.AlarmCode, a.Definition.Severity,
        a.Equipment.Title, a.Equipment.Area.Title);

    private static AlarmDto ToDto(Alarm a) => new(
        a.Id, a.Definition.Title, a.Definition.AlarmCode, a.Definition.Severity, a.Definition.Description,
        a.EquipmentId, a.Equipment.Title, a.Equipment.Area.Title,
        a.CreatedAt, a.UpdatedAt,
        a.Definition.Solutions.OrderBy(s => s.SortOrder).ThenBy(s => s.Id).Select(SolutionService.ToDto).ToList(),
        a.AlarmDefinitionId,
        a.Definition.Alarms.Where(x => x.Id != a.Id).Select(x => x.Equipment.Title).Order().ToList());
}
