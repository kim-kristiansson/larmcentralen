using Larmcentralen.Application.DTOs;

namespace Larmcentralen.Application.Interfaces;

public interface IAreaService
{
    Task<List<AreaDto>> GetAllAsync();
    Task<AreaDto?> GetByIdAsync(int id);
    Task<WriteResult<AreaDto>> CreateAsync(CreateAreaDto dto);
    Task<WriteResult<AreaDto>> UpdateAsync(int id, UpdateAreaDto dto);
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
/// <summary>The bin (Papperskorg) for removed avdelningar and units. See TrashService.</summary>
public interface ITrashService
{
    /// <summary>What removing it would take with it. Null when it does not exist.</summary>
    Task<TrashPreviewDto?> PreviewAsync(int? areaId, int? equipmentId);

    /// <summary>Moves an avdelning (areaId) or a unit (equipmentId) to the bin with everything below it.</summary>
    Task<WriteResult<TrashItemDto>> MoveToTrashAsync(int? areaId, int? equipmentId, DateTime? now = null);

    Task<List<TrashItemDto>> GetAllAsync();

    /// <summary>Brings a removal back. 409 when its avdelning is in the bin too, or a unit with its name was added since.</summary>
    Task<WriteResult<TrashItemDto>> RestoreAsync(int batchId);

    /// <summary>Erases a removal for good, now. False when it does not exist.</summary>
    Task<bool> EraseAsync(int batchId);

    /// <summary>Erases every removal older than the retention period. Returns how many.</summary>
    Task<int> EraseExpiredAsync(DateTime now);
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

public interface ISmsSettingsService
{
    Task<SmsSettingsDto> GetAsync();

    /// <summary>Normalises and saves the numbers. Invalid (with a Swedish reason) for a number that isn't Swedish.</summary>
    Task<WriteResult<SmsSettingsDto>> UpdateAsync(UpdateSmsSettingsDto dto, DateTime? now = null);

    /// <summary>Accepted only from a configured sender to the configured 46elks number.</summary>
    Task<SmsCheck> CheckIncomingAsync(string? from, string? to);

    /// <summary>Keeps what happened to the last SMS, for the admin page.</summary>
    Task RecordAsync(string? from, bool accepted, string result, DateTime? now = null);

    /// <summary>The 46elks number and where fetching from 46elks' SMS history stopped last time.</summary>
    /// <summary>
    /// The company's own 46elks login saved under SMS-larm, decrypted; null when none is saved. Throws
    /// InvalidOperationException (Swedish message) when the saved password can't be decrypted.
    /// </summary>
    Task<ElksLogin?> GetElksLoginAsync();

    Task<SmsFetchState> GetFetchStateAsync();

    /// <summary>Keeps when 46elks was last asked and how it went; moves the fetch mark forward (never back).</summary>
    Task RecordFetchAsync(DateTime at, DateTime? fetchedUntil, string? error);
}

/// <summary>Encrypts secrets kept in the database (the 46elks password). Implemented by the API.</summary>
public interface ISecretProtector
{
    string Protect(string secret);

    /// <summary>Null when it can't be decrypted (for example the server's keys were lost).</summary>
    string? Unprotect(string protectedSecret);
}

/// <summary>Contact people and their roles. Admin only.</summary>
public interface IContactService
{
    Task<List<ContactRoleDto>> GetRolesAsync();
    Task<WriteResult<ContactRoleDto>> CreateRoleAsync(SaveContactRoleDto dto);
    Task<WriteResult<ContactRoleDto>> UpdateRoleAsync(int id, SaveContactRoleDto dto);

    /// <summary>Conflict (value: the names of the people who have it) while any contact has the role.</summary>
    Task<WriteResult<List<string>>> DeleteRoleAsync(int id);

    Task<List<ContactDto>> GetContactsAsync();
    Task<ContactDto?> GetContactAsync(int id);
    Task<WriteResult<ContactDto>> CreateContactAsync(SaveContactDto dto);
    Task<WriteResult<ContactDto>> UpdateContactAsync(int id, SaveContactDto dto);
    Task<bool> DeleteContactAsync(int id);
}
