using Larmcentralen.Application.DTOs;
using Larmcentralen.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Larmcentralen.Api.Controllers;

/// <summary>The list of PLC alarm codes (M200.0, M200.1 …), each listed once. Adding one needs the admin key.</summary>
[ApiController]
[Route("api/alarm-codes")]
public class AlarmCodesController(IAlarmCodeService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<AlarmCodeDto>>> GetAll() => Ok(await service.GetAllAsync());

    /// <summary>Adds a code. 409 (with the existing code) when it is already in the list, in any case.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(CreateAlarmCodeDto dto)
    {
        var (status, code, error) = await service.CreateAsync(dto);
        return status switch
        {
            WriteStatus.Ok => Created($"api/alarm-codes/{code!.Id}", code),
            WriteStatus.Conflict => Conflict(new { error, code }),
            _ => this.ToError(status, error)
        };
    }
}
