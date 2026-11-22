using Larmcentralen.Api.Security;
using Larmcentralen.Application.DTOs;
using Larmcentralen.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Larmcentralen.Api.Controllers;

/// <summary>
/// Which numbers incoming alarm SMS must come from (the alarm system, OCSS) and go to (the 46elks number).
/// Admin only, reads included.
/// </summary>
[ApiController]
[Route("api/settings/sms")]
[Authorize(Policy = Policies.Edit)]
public class SmsSettingsController(ISmsSettingsService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SmsSettingsDto>> Get() => Ok(await service.GetAsync());

    /// <summary>400 (with a Swedish reason) when a number isn't a Swedish phone number.</summary>
    [HttpPut]
    public async Task<IActionResult> Update(UpdateSmsSettingsDto dto)
    {
        var result = await service.UpdateAsync(dto);
        return result.Status == WriteStatus.Ok ? Ok(result.Value) : this.ToError(result.Status, result.Error);
    }
}
