using Larmcentralen.Api.Security;
using Larmcentralen.Application.DTOs;
using Larmcentralen.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Larmcentralen.Api.Controllers;

/// <summary>
/// The bin (Papperskorg). Admin only, reads included: removed avdelningar and units are not for operators.
/// Things get here through DELETE api/areas/{id} and DELETE api/equipment/{id}.
/// </summary>
[ApiController]
[Route("api/trash")]
[Authorize(Policy = Policies.Edit)]
public class TrashController(ITrashService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<TrashItemDto>>> GetAll() => Ok(await service.GetAllAsync());

    /// <summary>What removing the avdelning would take with it.</summary>
    [HttpGet("preview/areas/{id:int}")]
    public async Task<ActionResult<TrashPreviewDto>> PreviewArea(int id)
        => await service.PreviewAsync(id, null) is { } preview ? Ok(preview) : NotFound();

    /// <summary>What removing the unit would take with it.</summary>
    [HttpGet("preview/equipment/{id:int}")]
    public async Task<ActionResult<TrashPreviewDto>> PreviewEquipment(int id)
        => await service.PreviewAsync(null, id) is { } preview ? Ok(preview) : NotFound();

    /// <summary>Brings it back with everything that went with it. 409 when that is not possible yet (the reason says why).</summary>
    [HttpPost("{id:int}/restore")]
    public async Task<IActionResult> Restore(int id)
    {
        var result = await service.RestoreAsync(id);
        return result.Status == WriteStatus.Ok ? Ok(result.Value) : this.ToError(result.Status, result.Error);
    }

    /// <summary>Erases it for good, now, instead of waiting for the retention period.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Erase(int id) => await service.EraseAsync(id) ? NoContent() : NotFound();
}
