using Larmcentralen.Application.DTOs;
using Larmcentralen.Application.Interfaces;
using Larmcentralen.Domain.Interfaces;

namespace Larmcentralen.Application.Services;

public class SyncService(
    IAreaRepository areas,
    IEquipmentRepository equipment,
    IAlarmRepository alarms,
    ISolutionRepository solutions) : ISyncService
{
    public async Task<SyncSnapshotDto> GetSnapshotAsync()
    {
        // Sequential on purpose: the repositories share one scoped DbContext.
        var areaList = await areas.GetAllAsync();
        var equipmentList = await equipment.GetAllAsync();
        var alarmList = await alarms.GetAllAsync();
        var solutionList = await solutions.GetAllAsync();

        return new SyncSnapshotDto(
            DateTime.UtcNow,
            areaList.Select(a => new SyncAreaDto(a.Id, a.Title, a.DisplayName, a.UpdatedAt)).ToList(),
            equipmentList.Select(e => new SyncEquipmentDto(e.Id, e.Title, e.DisplayName, e.AreaId, e.UpdatedAt)).ToList(),
            alarmList.Select(a => new SyncAlarmDto(
                a.Id, a.Title, a.AlarmCode, a.Severity, a.Description,
                a.EquipmentId, a.CreatedAt, a.UpdatedAt)).ToList(),
            solutionList.Select(s => new SyncSolutionDto(
                s.Id, s.Title, s.Content, s.SortOrder, s.EstimatedTime,
                s.AlarmId, s.CreatedAt, s.UpdatedAt)).ToList());
    }
}
