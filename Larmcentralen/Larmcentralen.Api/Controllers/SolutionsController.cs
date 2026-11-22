using Larmcentralen.Application.DTOs;
using Larmcentralen.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Larmcentralen.Api.Controllers;

/// <summary>
/// Solutions. A solution can be shared by several alarms; every change refreshes the SharePoint
/// document of each alarm that showed it before or shows it after.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SolutionsController(
    ISolutionService service,
    ISharePointSyncService syncService) : ControllerBase
{
    [HttpGet("by-alarm/{alarmId:int}")]
    public async Task<ActionResult<List<SolutionDto>>> GetByAlarm(int alarmId)
        => Ok(await service.GetByAlarmIdAsync(alarmId));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SolutionDto>> Get(int id)
    {
        var solution = await service.GetByIdAsync(id);
        return solution is null ? NotFound() : Ok(solution);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateSolutionDto dto)
    {
        var result = await service.CreateAsync(dto);
        if (result.Status != SolutionWriteStatus.Ok) return this.ToError(result);
        await this.SyncAlarmsAsync(syncService, result);
        return CreatedAtAction(nameof(Get), new { id = result.Solution!.Id }, result.Solution);
    }

    /// <summary>AlarmIds null leaves the links unchanged; a list replaces them (and must not be empty).</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateSolutionDto dto)
    {
        var result = await service.UpdateAsync(id, dto);
        if (result.Status != SolutionWriteStatus.Ok) return this.ToError(result);
        await this.SyncAlarmsAsync(syncService, result);
        return NoContent();
    }

    /// <summary>Deletes the solution for every alarm it is linked to.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await service.DeleteAsync(id);
        if (result.Status != SolutionWriteStatus.Ok) return this.ToError(result);
        await this.SyncAlarmsAsync(syncService, result);
        return NoContent();
    }
}

internal static class SolutionResultExtensions
{
    public static IActionResult ToError(this ControllerBase c, SolutionWriteResult result) => result.Status switch
    {
        SolutionWriteStatus.NotFound => c.NotFound(),
        SolutionWriteStatus.Invalid => c.Problem(result.Error, statusCode: StatusCodes.Status400BadRequest),
        SolutionWriteStatus.Conflict => c.Problem(result.Error, statusCode: StatusCodes.Status409Conflict),
        _ => c.StatusCode(StatusCodes.Status500InternalServerError)
    };

    public static async Task SyncAlarmsAsync(this ControllerBase _, ISharePointSyncService sync, SolutionWriteResult result)
    {
        foreach (var alarmId in result.AffectedAlarmIds ?? [])
            await sync.SyncAlarmAsync(alarmId);
    }
}
