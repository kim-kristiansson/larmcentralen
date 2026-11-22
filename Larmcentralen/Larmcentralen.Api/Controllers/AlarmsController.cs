using Larmcentralen.Api.Services;
using Larmcentralen.Application.DTOs;
using Larmcentralen.Application.Interfaces;
using Larmcentralen.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Larmcentralen.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AlarmsController(IAlarmService service) : ControllerBase
{
    /// <summary>Removes a shared solution from this alarm only. 409 when it is the solution's last alarm.</summary>
    [HttpDelete("{alarmId:int}/solutions/{solutionId:int}")]
    public async Task<IActionResult> UnlinkSolution(int alarmId, int solutionId,
        [FromServices] ISolutionService solutions, [FromServices] ISharePointSyncService sync)
    {
        var result = await solutions.UnlinkAsync(alarmId, solutionId);
        if (result.Status != SolutionWriteStatus.Ok) return this.ToError(result);
        await this.SyncAlarmsAsync(sync, result);
        return NoContent();
    }

    /// <summary>
    /// Gives this alarm its own copy of a shared solution, so it can be edited without changing
    /// the other units. Returns the copy.
    /// </summary>
    [HttpPost("{alarmId:int}/solutions/{solutionId:int}/detach")]
    public async Task<IActionResult> DetachSolution(int alarmId, int solutionId,
        [FromServices] ISolutionService solutions, [FromServices] ISharePointSyncService sync)
    {
        var result = await solutions.DetachAsync(alarmId, solutionId);
        if (result.Status != SolutionWriteStatus.Ok) return this.ToError(result);
        await this.SyncAlarmsAsync(sync, result);
        return Ok(result.Solution);
    }

    [HttpGet]
    public async Task<ActionResult<List<AlarmListDto>>> GetAll(
        [FromQuery] string? search,
        [FromQuery] int? equipmentId,
        [FromQuery] int? areaId,
        [FromQuery] string? severity,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 10)
        => Ok(await service.SearchAsync(search, equipmentId, areaId, severity, skip, take));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AlarmDto>> Get(int id)
    {
        var alarm = await service.GetByIdAsync(id);
        return alarm is null ? NotFound() : Ok(alarm);
    }

    [HttpPost]
    public async Task<ActionResult<AlarmDto>> Create(CreateAlarmDto dto)
    {
        var alarm = await service.CreateAsync(dto);
        return CreatedAtAction(nameof(Get), new { id = alarm.Id }, alarm);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateAlarmDto dto)
        => await service.UpdateAsync(id, dto) ? NoContent() : NotFound();

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
        => await service.DeleteAsync(id) ? NoContent() : NotFound();
    
    [HttpGet("{id}/export")]
    public async Task<IActionResult> ExportToWord(int id, [FromServices] ExportService exportService)
    {
        var result = await exportService.ExportAlarmToDocxAsync(id);
        if (result is null) return NotFound();

        var (bytes, fileName) = result.Value;
        return File(bytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", fileName);
    }
    
    [HttpPost("{id}/upload-sharepoint")]
    public async Task<IActionResult> UploadToSharePoint(
        int id,
        [FromServices] ExportService exportService,
        [FromServices] SharePointUploadService sharePointService)
    {
        var result = await exportService.ExportAlarmToDocxAsync(id);
        if (result is null) return NotFound();

        var (bytes, fileName) = result.Value;
        var url = await sharePointService.UploadAsync(bytes, fileName, "Larmcentralen");

        if (url is null)
            return StatusCode(500, "Could not upload to SharePoint");

        return Ok(new { url });
    }
    
    [HttpGet("by-ids")]
    public async Task<ActionResult<List<AlarmListDto>>> GetByIds([FromQuery] List<int> ids)
    {
        var alarms = await service.GetByIdsAsync(ids);
        return Ok(alarms);
    }
}