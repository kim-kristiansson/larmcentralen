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
    /// <summary>
    /// 46elks posts each incoming SMS here (form fields from, to, message). Always an empty 200, also for SMS that
    /// are turned away: 46elks would send any text in the response back to the sender as a reply SMS.
    /// </summary>
    [HttpPost("sms")]
    public async Task<IActionResult> ReceiveSms([FromForm] string? from, [FromForm] string? to, [FromForm] string? message)
    {
        await smsService.ProcessSmsAsync(from, to, message);
        return Ok();
    }
}
