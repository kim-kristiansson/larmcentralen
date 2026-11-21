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
        if (result.Status != WriteStatus.Ok) return this.ToError(result);
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
        if (result.Status != WriteStatus.Ok) return this.ToError(result);
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

    /// <summary>The new alarm joins the definition with the same code and text on units of its type, or starts one.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(CreateAlarmDto dto)
    {
        var result = await service.CreateAsync(dto);
        if (result.Status != WriteStatus.Ok) return this.ToError(result.Status, result.Error);
        return CreatedAtAction(nameof(Get), new { id = result.Alarm!.Id }, result.Alarm);
    }

    /// <summary>
    /// Text, code, severity and description belong to the alarm's definition: they change for every unit
    /// that shares it. 409 when another definition of the same type already has that code and text.
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateAlarmDto dto)
    {
        var result = await service.UpdateAsync(id, dto);
        return result.Status == WriteStatus.Ok ? NoContent() : this.ToError(result.Status, result.Error);
    }

    /// <summary>Gives this unit its own definition (a copy, with the same solutions), so it can be changed alone.</summary>
    [HttpPost("{id:int}/own-definition")]
    public async Task<IActionResult> Split(int id)
    {
        var result = await service.SplitAsync(id);
        return result.Status == WriteStatus.Ok ? Ok(result.Alarm) : this.ToError(result.Status, result.Error);
    }

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