using Larmcentralen.Application.DTOs;
using Larmcentralen.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Larmcentralen.Api.Controllers;

/// <summary>Units (T101, A01, W1 …). Reading needs any key; changes need the admin key.</summary>
[ApiController]
[Route("api/[controller]")]
public class EquipmentController(IEquipmentService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<EquipmentDto>>> GetAll([FromQuery] int? areaId)
        => Ok(await service.GetAllAsync(areaId));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<EquipmentDto>> Get(int id)
    {
        var equipment = await service.GetByIdAsync(id);
        return equipment is null ? NotFound() : Ok(equipment);
    }

    /// <summary>400 without a title or with an unknown area, 409 when another unit already has the title.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(CreateEquipmentDto dto)
    {
        var result = await service.CreateAsync(dto);
        return result.Status == WriteStatus.Ok
            ? CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value)
            : this.ToError(result.Status, result.Error);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateEquipmentDto dto)
    {
        var result = await service.UpdateAsync(id, dto);
        return result.Status == WriteStatus.Ok ? NoContent() : this.ToError(result.Status, result.Error);
    }

    /// <summary>Moves the unit to the bin (Papperskorg) with its alarms and solutions. Returns the bin entry.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, [FromServices] ITrashService trash)
    {
        var result = await trash.MoveToTrashAsync(null, id);
        return result.Status == WriteStatus.Ok ? Ok(result.Value) : this.ToError(result.Status, result.Error);
    }
}
