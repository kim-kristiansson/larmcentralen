using Larmcentralen.Application.DTOs;
using Larmcentralen.Application.Interfaces;
using Larmcentralen.Domain.Entities;
using Larmcentralen.Domain.Interfaces;

namespace Larmcentralen.Application.Services;

/// <summary>The list of PLC alarm codes. New codes can be added from the app; they are stored upper case and unique.</summary>
public class AlarmCodeService(IAlarmCodeRepository repo) : IAlarmCodeService
{
    public async Task<List<AlarmCodeDto>> GetAllAsync()
        => (await repo.GetAllWithAlarmsAsync()).Select(c => new AlarmCodeDto(c.Id, c.Code, c.Description, c.Alarms.Count)).ToList();

    public async Task<(WriteStatus Status, AlarmCodeDto? Code, string? Error)> CreateAsync(CreateAlarmCodeDto dto)
    {
        var code = AlarmText.NormaliseCode(dto.Code);
        if (code is null) return (WriteStatus.Invalid, null, "Ange larmkoden.");
        if (code.Length > 50) return (WriteStatus.Invalid, null, "Larmkoden får vara högst 50 tecken.");
        if (await repo.FindAsync(code) is { } existing)
            return (WriteStatus.Conflict, new AlarmCodeDto(existing.Id, existing.Code, existing.Description, 0), $"Larmkoden {code} finns redan.");

        var created = new AlarmCode
        {
            Code = code,
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim()
        };
        await repo.AddAsync(created);
        await repo.SaveChangesAsync();
        return (WriteStatus.Ok, new AlarmCodeDto(created.Id, created.Code, created.Description, 0), null);
    }
}
