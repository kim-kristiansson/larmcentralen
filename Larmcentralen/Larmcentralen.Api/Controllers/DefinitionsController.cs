using Larmcentralen.Application.DTOs;
using Larmcentralen.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Larmcentralen.Api.Controllers;

/// <summary>
/// Solution links on alarm definitions directly. The same calls under /api/alarms/{id}/solutions act on
/// that alarm's definition.
/// </summary>
[ApiController]
[Route("api/definitions")]
public class DefinitionsController(ISolutionService solutions, ISharePointSyncService sync) : ControllerBase
{
    /// <summary>Removes a shared solution from this definition (every unit that shares it). 409 on the last link.</summary>
    [HttpDelete("{definitionId:int}/solutions/{solutionId:int}")]
    public async Task<IActionResult> UnlinkSolution(int definitionId, int solutionId)
    {
        var result = await solutions.UnlinkDefinitionAsync(definitionId, solutionId);
        if (result.Status != WriteStatus.Ok) return this.ToError(result);
        await this.SyncAlarmsAsync(sync, result);
        return NoContent();
    }

    /// <summary>Gives this definition its own copy of a shared solution. Returns the copy.</summary>
    [HttpPost("{definitionId:int}/solutions/{solutionId:int}/detach")]
    public async Task<IActionResult> DetachSolution(int definitionId, int solutionId)
    {
        var result = await solutions.DetachDefinitionAsync(definitionId, solutionId);
        if (result.Status != WriteStatus.Ok) return this.ToError(result);
        await this.SyncAlarmsAsync(sync, result);
        return Ok(result.Solution);
    }
}
