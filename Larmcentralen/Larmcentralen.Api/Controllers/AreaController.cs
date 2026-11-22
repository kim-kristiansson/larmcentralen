using Larmcentralen.Application.DTOs;
using Larmcentralen.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Larmcentralen.Api.Controllers;

/// <summary>Areas (e.g. "SQ-linjen (OCSS)"). Reading needs any key; changes need the admin key.</summary>
[ApiController]
[Route("api/[controller]")]
public class AreasController(IAreaService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<AreaDto>>> GetAll()
        => Ok(await service.GetAllAsync());

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AreaDto>> Get(int id)
    {
        var area = await service.GetByIdAsync(id);
        return area is null ? NotFound() : Ok(area);
    }

    /// <summary>400 without a name, 409 when another area already has it.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(CreateAreaDto dto)
    {
        var result = await service.CreateAsync(dto);
        return result.Status == WriteStatus.Ok
            ? CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value)
            : this.ToError(result.Status, result.Error);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateAreaDto dto)
    {
        var result = await service.UpdateAsync(id, dto);
        return result.Status == WriteStatus.Ok ? NoContent() : this.ToError(result.Status, result.Error);
    }

    /// <summary>409 while the area still has units.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await service.DeleteAsync(id);
        return result.Status == WriteStatus.Ok ? NoContent() : this.ToError(result.Status, result.Error);
    }
}
