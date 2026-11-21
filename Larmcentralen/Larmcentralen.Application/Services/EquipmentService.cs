using Larmcentralen.Application.DTOs;
using Larmcentralen.Application.Interfaces;
using Larmcentralen.Domain.Entities;
using Larmcentralen.Domain.Interfaces;

namespace Larmcentralen.Application.Services;

public class EquipmentService(
    IEquipmentRepository repo,
    IAlarmRepository alarms,
    IAlarmDefinitionRepository definitions,
    ISolutionRepository solutions) : IEquipmentService
{
    public async Task<List<EquipmentDto>> GetAllAsync(int? areaId)
    {
        var items = areaId.HasValue
            ? await repo.GetByAreaIdAsync(areaId.Value)
            : await repo.GetAllAsync();

        return items.Select(e => new EquipmentDto(
            e.Id, e.Title, e.DisplayName, e.Category, e.Description,
            e.AreaId, e.Area.Title, e.Alarms.Count, e.EquipmentType
        )).ToList();
    }

    public async Task<EquipmentDto?> GetByIdAsync(int id)
    {
        var e = await repo.GetByIdWithDetailsAsync(id);
        if (e is null) return null;

        return new EquipmentDto(
            e.Id, e.Title, e.DisplayName, e.Category, e.Description,
            e.AreaId, e.Area.Title, e.Alarms.Count, e.EquipmentType
        );
    }

    public async Task<EquipmentDto> CreateAsync(CreateEquipmentDto dto)
    {
        var equipment = new Equipment
        {
            Title = dto.Title,
            DisplayName = dto.DisplayName,
            Category = dto.Category,
            Description = dto.Description,
            AreaId = dto.AreaId,
            EquipmentType = NormaliseType(dto.EquipmentType)
        };

        await repo.AddAsync(equipment);
        await repo.SaveChangesAsync();

        var created = await repo.GetByIdWithDetailsAsync(equipment.Id);
        return new EquipmentDto(
            created!.Id, created.Title, created.DisplayName, created.Category, created.Description,
            created.AreaId, created.Area.Title, 0, created.EquipmentType
        );
    }

    public async Task<bool> UpdateAsync(int id, UpdateEquipmentDto dto)
    {
        var equipment = await repo.GetByIdAsync(id);
        if (equipment is null) return false;

        equipment.Title = dto.Title;
        equipment.DisplayName = dto.DisplayName;
        equipment.Category = dto.Category;
        equipment.Description = dto.Description;
        equipment.AreaId = dto.AreaId;
        if (dto.EquipmentType is not null && NormaliseType(dto.EquipmentType) != equipment.EquipmentType)
        {
            equipment.EquipmentType = NormaliseType(dto.EquipmentType);
            await MoveAlarmsToNewTypeAsync(equipment);
        }
        equipment.UpdatedAt = DateTime.UtcNow;

        await repo.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var equipment = await repo.GetByIdAsync(id);
        if (equipment is null) return false;

        repo.Remove(equipment);
        await repo.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// After a unit changes type, files each of its alarms under a definition of the new type: one with the
    /// same code and text if it exists, otherwise its own definition moves (or a copy when other units share it).
    /// The alarms keep their solutions.
    /// </summary>
    private async Task MoveAlarmsToNewTypeAsync(Equipment unit)
    {
        var type = AlarmDefinition.TypeFor(unit);
        foreach (var alarmId in (await repo.GetByIdWithDetailsAsync(unit.Id))!.Alarms.Select(a => a.Id).ToList())
        {
            var alarm = (await alarms.GetForUpdateAsync(alarmId))!;
            var old = alarm.Definition;
            // A definition split off for this unit stays its own; one it only had for being "Övrigt" becomes shareable.
            var splitOff = old.EquipmentType != AlarmDefinition.OtherType && old.ScopeEquipmentId == unit.Id;
            var scope = splitOff ? unit.Id : AlarmDefinition.DefaultScopeFor(unit);
            var target = await definitions.FindAsync(type, old.AlarmCode, old.NormalizedTitle, scope);
            if (target == old) continue; // e.g. no type before and "Övrigt" now: the same definition
            if (target is null && old.Alarms.All(a => a == alarm))
            {
                old.EquipmentType = type;
                old.ScopeEquipmentId = scope;
                old.UpdatedAt = DateTime.UtcNow;
                continue;
            }
            if (target is null)
            {
                target = new AlarmDefinition
                {
                    EquipmentType = type, ScopeEquipmentId = scope, AlarmCode = old.AlarmCode, Title = old.Title,
                    NormalizedTitle = old.NormalizedTitle, Severity = old.Severity, Description = old.Description
                };
                await definitions.AddAsync(target);
            }
            foreach (var s in old.Solutions.ToList()) AlarmDefinitions.Link(target, s);
            alarm.Definition = target;
            AlarmDefinitions.RemoveIfUnused(old, alarm, definitions, solutions);
        }
    }

    private static string? NormaliseType(string? type) => string.IsNullOrWhiteSpace(type) ? null : type.Trim();
}
