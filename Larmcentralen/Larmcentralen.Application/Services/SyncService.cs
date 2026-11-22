using Larmcentralen.Application.DTOs;
using Larmcentralen.Application.Interfaces;
using Larmcentralen.Domain.Interfaces;

namespace Larmcentralen.Application.Services;

public class SyncService(
    IAreaRepository areas,
    IEquipmentRepository equipment,
    IAlarmRepository alarms,
    ISolutionRepository solutions,
    IAlarmCodeRepository codes) : ISyncService
{
    public async Task<SyncSnapshotDto> GetSnapshotAsync()
    {
        // Sequential on purpose: the repositories share one scoped DbContext.
        var areaList = await areas.GetAllAsync();
        var equipmentList = await equipment.GetAllAsync();
        var alarmList = await alarms.GetAllAsync();
        var solutionList = await solutions.GetAllAsync();
        var links = await solutions.GetAllLinksAsync();
        var codeList = await codes.GetAllAsync();

        // Older clients only know Solution.AlarmId: give them the lowest linked alarm.
        var firstAlarm = links.GroupBy(l => l.SolutionId).ToDictionary(g => g.Key, g => g.Min(l => l.AlarmId));

        return new SyncSnapshotDto(
            DateTime.UtcNow,
            areaList.Select(a => new SyncAreaDto(a.Id, a.Title, a.DisplayName, a.UpdatedAt)).ToList(),
            equipmentList.Select(e => new SyncEquipmentDto(e.Id, e.Title, e.DisplayName, e.AreaId, e.UpdatedAt, e.EquipmentType)).ToList(),
            alarmList.Select(a => new SyncAlarmDto(
                a.Id, a.Title, a.AlarmCode?.Code, a.Severity, a.Description,
                a.EquipmentId, a.CreatedAt, a.UpdatedAt, a.AlarmCodeId)).ToList(),
            solutionList.Select(s => new SyncSolutionDto(
                s.Id, s.Title, s.Content, s.SortOrder, s.EstimatedTime,
                firstAlarm.GetValueOrDefault(s.Id), s.CreatedAt, s.UpdatedAt)).ToList(),
            links.Select(l => new SyncAlarmSolutionDto(l.AlarmId, l.SolutionId)).ToList(),
            codeList.Select(c => new SyncAlarmCodeDto(c.Id, c.Code, c.Description)).ToList());
    }
}
