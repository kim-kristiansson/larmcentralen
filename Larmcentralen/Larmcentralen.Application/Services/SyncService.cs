using Larmcentralen.Application.DTOs;
using Larmcentralen.Application.Interfaces;
using Larmcentralen.Domain.Interfaces;

namespace Larmcentralen.Application.Services;

public class SyncService(
    IAreaRepository areas,
    IEquipmentRepository equipment,
    IAlarmRepository alarms,
    IAlarmDefinitionRepository definitions,
    ISolutionRepository solutions) : ISyncService
{
    public async Task<SyncSnapshotDto> GetSnapshotAsync()
    {
        // Sequential on purpose: the repositories share one scoped DbContext.
        var areaList = await areas.GetAllAsync();
        var equipmentList = await equipment.GetAllAsync();
        var alarmList = await alarms.GetAllAsync();
        var definitionList = await definitions.GetAllAsync();
        var solutionList = await solutions.GetAllAsync();
        var links = await definitions.GetAllLinksAsync();

        var definitionById = definitionList.ToDictionary(d => d.Id);
        var alarmsByDefinition = alarmList.ToLookup(a => a.AlarmDefinitionId, a => a.Id);

        // Older clients know solutions per alarm: expand each definition link to its alarms.
        var alarmLinks = links
            .SelectMany(l => alarmsByDefinition[l.AlarmDefinitionId].Select(alarmId => new SyncAlarmSolutionDto(alarmId, l.SolutionId)))
            .Distinct()
            .OrderBy(l => l.SolutionId).ThenBy(l => l.AlarmId)
            .ToList();
        var firstAlarm = alarmLinks.GroupBy(l => l.SolutionId).ToDictionary(g => g.Key, g => g.Min(l => l.AlarmId));

        return new SyncSnapshotDto(
            DateTime.UtcNow,
            areaList.Select(a => new SyncAreaDto(a.Id, a.Title, a.DisplayName, a.UpdatedAt)).ToList(),
            equipmentList.Select(e => new SyncEquipmentDto(e.Id, e.Title, e.DisplayName, e.AreaId, e.UpdatedAt, e.EquipmentType)).ToList(),
            alarmList.Select(a =>
            {
                var d = definitionById[a.AlarmDefinitionId];
                return new SyncAlarmDto(
                    a.Id, d.Title, d.AlarmCode, d.Severity, d.Description,
                    a.EquipmentId, a.CreatedAt, Latest(a.UpdatedAt, d.UpdatedAt), a.AlarmDefinitionId);
            }).ToList(),
            solutionList.Select(s => new SyncSolutionDto(
                s.Id, s.Title, s.Content, s.SortOrder, s.EstimatedTime,
                firstAlarm.GetValueOrDefault(s.Id), s.CreatedAt, s.UpdatedAt)).ToList(),
            alarmLinks,
            definitionList.Select(d => new SyncAlarmDefinitionDto(
                d.Id, d.EquipmentType, d.ScopeEquipmentId, d.AlarmCode, d.Title, d.Severity, d.Description, d.UpdatedAt)).ToList(),
            links.Select(l => new SyncDefinitionSolutionDto(l.AlarmDefinitionId, l.SolutionId)).ToList());
    }

    private static DateTime? Latest(DateTime? a, DateTime? b) => a is null ? b : b is null ? a : (a > b ? a : b);
}
