using Larmcentralen.Api.Security;
using Larmcentralen.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Larmcentralen.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous] // 46elks has no API key; the secret in the URL protects it instead.
[RequireWebhookSecret]
public class WebhooksController(SmsAlarmService smsService) : ControllerBase
{
    [HttpPost("sms")]
    public async Task<IActionResult> ReceiveSms([FromForm] string from, [FromForm] string message)
    {
        await smsService.ProcessSmsAsync(from, message);
        return Ok();
    }
}