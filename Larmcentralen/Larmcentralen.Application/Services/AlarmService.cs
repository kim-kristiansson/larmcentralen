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

        AlarmDefinition definition;
        if (dto.AlarmDefinitionId is { } picked)
        {
            var (found, error) = await PickAsync(picked, unit, exceptAlarmId: null);
            if (error is not null) return error;
            definition = found!;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(dto.Title)) return AlarmWriteResult.Invalid("Ange larmtexten.");
            definition = await AlarmDefinitions.ResolveAsync(definitions, unit, dto.AlarmCode, dto.Title, dto.Severity, dto.Description);
            if (definition.Id != 0 && await repo.ExistsAsync(definition.Id, unit.Id))
                return AlarmWriteResult.Conflict("Larmet finns redan på den här enheten.");
        }

        var alarm = new Alarm { EquipmentId = unit.Id, Definition = definition };
        await repo.AddAsync(alarm);
        await repo.SaveChangesAsync();

        return new AlarmWriteResult(WriteStatus.Ok, ToDto((await repo.GetByIdWithDetailsAsync(alarm.Id))!));
    }

    public async Task<AlarmWriteResult> UpdateAsync(int id, UpdateAlarmDto dto)
    {
        var alarm = await repo.GetForUpdateAsync(id);
        if (alarm is null) return AlarmWriteResult.NotFound();

        var old = alarm.Definition;

        if (dto.AlarmDefinitionId is { } picked && picked != old.Id)
        {
            // Another code picked from the list: the alarm now means that. The old definition's solutions stay
            // with it, and it goes (with the solutions only it had) when no other unit uses it.
            var unit = dto.EquipmentId == alarm.EquipmentId ? alarm.Equipment : await equipment.GetByIdAsync(dto.EquipmentId);
            if (unit is null) return AlarmWriteResult.Invalid("Utrustningen finns inte.");
            var (target, error) = await PickAsync(picked, unit, exceptAlarmId: alarm.Id);
            if (error is not null) return error;
            MoveTo(alarm, unit, target!, old);
        }
        else if (dto.NewDefinition)
        {
            // "Ny larmkod": the definition for the code and text typed (a new one unless one already has them).
            if (string.IsNullOrWhiteSpace(dto.Title)) return AlarmWriteResult.Invalid("Ange larmtexten.");
            var unit = dto.EquipmentId == alarm.EquipmentId ? alarm.Equipment : await equipment.GetByIdAsync(dto.EquipmentId);
            if (unit is null) return AlarmWriteResult.Invalid("Utrustningen finns inte.");
            var target = await AlarmDefinitions.ResolveAsync(definitions, unit, dto.AlarmCode, dto.Title, dto.Severity, dto.Description);
            if (target != old)
            {
                if (target.Id != 0 && (await definitions.GetByIdWithDetailsAsync(target.Id))!.Alarms.Any(a => a.EquipmentId == unit.Id && a.Id != alarm.Id))
                    return AlarmWriteResult.Conflict("Larmet finns redan på den här enheten.");
                MoveTo(alarm, unit, target, old);
            }
            else alarm.EquipmentId = unit.Id;
        }
        else if (string.IsNullOrWhiteSpace(dto.Title))
        {
            return AlarmWriteResult.Invalid("Ange larmtexten.");
        }
        else if (dto.EquipmentId != alarm.EquipmentId)
        {
            // Moving to another unit: the alarm takes that unit's definition for this code and text.
            var unit = await equipment.GetByIdAsync(dto.EquipmentId);
            if (unit is null) return AlarmWriteResult.Invalid("Utrustningen finns inte.");
            var target = await AlarmDefinitions.ResolveAsync(definitions, unit, dto.AlarmCode, dto.Title, dto.Severity, dto.Description);
            if (target.Id != 0 && await repo.ExistsAsync(target.Id, unit.Id))
                return AlarmWriteResult.Conflict("Larmet finns redan på den enheten.");

            // It keeps the solutions it had.
            foreach (var s in old.Solutions.ToList()) AlarmDefinitions.Link(target, s);
            alarm.EquipmentId = unit.Id;
            alarm.Definition = target;
            alarm.UpdatedAt = DateTime.UtcNow;
            if (target != old) AlarmDefinitions.RemoveIfUnused(old, alarm, definitions, solutions);
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
        repo.Remove(alarm);
        AlarmDefinitions.RemoveIfUnused(definition, alarm, definitions, solutions);
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
        if (shared.Alarms.Any(a => a != alarm))
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
            alarm.Definition = own;
            alarm.UpdatedAt = DateTime.UtcNow;
            await repo.SaveChangesAsync();
        }

        return new AlarmWriteResult(WriteStatus.Ok, ToDto((await repo.GetByIdWithDetailsAsync(id))!));
    }

    /// <summary>
    /// Checks that <paramref name="unit"/> may use a definition picked from the list: it exists, it is not owned by
    /// another unit, and the unit has no other alarm with it yet.
    /// </summary>
    private async Task<(AlarmDefinition? Definition, AlarmWriteResult? Error)> PickAsync(int definitionId, Equipment unit, int? exceptAlarmId)
    {
        var definition = await definitions.GetByIdWithDetailsAsync(definitionId);
        if (definition is null)
            return (null, AlarmWriteResult.Invalid("Larmkoden finns inte."));
        if (definition.ScopeEquipmentId != 0 && definition.ScopeEquipmentId != unit.Id)
            return (null, AlarmWriteResult.Invalid("Larmkoden gäller bara en annan enhet."));
        if (definition.Alarms.Any(a => a.EquipmentId == unit.Id && a.Id != exceptAlarmId))
            return (null, AlarmWriteResult.Conflict("Larmet finns redan på den här enheten."));
        return (definition, null);
    }

    /// <summary>Points the alarm at another definition. Solutions stay with the definitions they belong to.</summary>
    private void MoveTo(Alarm alarm, Equipment unit, AlarmDefinition target, AlarmDefinition old)
    {
        alarm.EquipmentId = unit.Id;
        alarm.Definition = target;
        alarm.UpdatedAt = DateTime.UtcNow;
        AlarmDefinitions.RemoveIfUnused(old, alarm, definitions, solutions);
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
