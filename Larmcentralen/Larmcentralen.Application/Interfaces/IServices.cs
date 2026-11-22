using Larmcentralen.Application.DTOs;

namespace Larmcentralen.Application.Interfaces;

public interface IAreaService
{
    Task<List<AreaDto>> GetAllAsync();
    Task<AreaDto?> GetByIdAsync(int id);
    Task<WriteResult<AreaDto>> CreateAsync(CreateAreaDto dto);
    Task<WriteResult<AreaDto>> UpdateAsync(int id, UpdateAreaDto dto);
    Task<WriteResult<AreaDto>> DeleteAsync(int id);
}

public interface IEquipmentService
{
    Task<List<EquipmentDto>> GetAllAsync(int? areaId);
    Task<EquipmentDto?> GetByIdAsync(int id);
    Task<WriteResult<EquipmentDto>> CreateAsync(CreateEquipmentDto dto);
    Task<WriteResult<EquipmentDto>> UpdateAsync(int id, UpdateEquipmentDto dto);
    Task<WriteResult<EquipmentDto>> DeleteAsync(int id);
}

public interface IAlarmService
{
    Task<List<AlarmListDto>> SearchAsync(string? search, int? equipmentId, int? areaId, string? severity, int skip = 0, int take = 10);
    Task<AlarmDto?> GetByIdAsync(int id);
    Task<AlarmWriteResult> CreateAsync(CreateAlarmDto dto);
    Task<AlarmWriteResult> UpdateAsync(int id, UpdateAlarmDto dto);
    Task<bool> DeleteAsync(int id);
    Task<List<AlarmListDto>> GetByIdsAsync(List<int> ids);
}

public interface ISolutionService
{
    Task<List<SolutionDto>> GetByAlarmIdAsync(int alarmId);
    Task<SolutionDto?> GetByIdAsync(int id);
    Task<SolutionWriteResult> CreateAsync(CreateSolutionDto dto);
    Task<SolutionWriteResult> UpdateAsync(int id, UpdateSolutionDto dto);
    Task<SolutionWriteResult> DeleteAsync(int id);
    Task<SolutionWriteResult> UnlinkAsync(int alarmId, int solutionId);
    Task<SolutionWriteResult> DetachAsync(int alarmId, int solutionId);
}
public interface ISyncService
{
    Task<SyncSnapshotDto> GetSnapshotAsync();
}

public interface IAlarmCodeService
{
    Task<List<AlarmCodeDto>> GetAllAsync();
    Task<(WriteStatus Status, AlarmCodeDto? Code, string? Error)> CreateAsync(CreateAlarmCodeDto dto);
}
