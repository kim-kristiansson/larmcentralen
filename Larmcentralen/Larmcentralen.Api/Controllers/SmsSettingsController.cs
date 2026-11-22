using Larmcentralen.Api.Security;
using Larmcentralen.Api.Services;
using Larmcentralen.Application.DTOs;
using Larmcentralen.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Larmcentralen.Api.Controllers;

/// <summary>
/// Which numbers incoming alarm SMS must come from (the alarm system, OCSS) and go to (the 46elks number), and the
/// company's own 46elks login. Admin only, reads included. The password is write-only.
/// </summary>
[ApiController]
[Route("api/settings/sms")]
[Authorize(Policy = Policies.Edit)]
public class SmsSettingsController(ISmsSettingsService service, ElksAccount account) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SmsSettingsDto>> Get()
        => Ok(await service.GetAsync() with { ServerHasElksAccount = account.ServerHasLogin });

    /// <summary>400 (with a Swedish reason) when a number isn't a Swedish phone number, or the login is half filled in.</summary>
    [HttpPut]
    public async Task<IActionResult> Update(UpdateSmsSettingsDto dto)
    {
        var result = await service.UpdateAsync(dto);
        return result.Status == WriteStatus.Ok
            ? Ok(result.Value! with { ServerHasElksAccount = account.ServerHasLogin })
            : this.ToError(result.Status, result.Error);
    }

    /// <summary>
    /// Logs in to 46elks with the saved login and checks that the 46elks number is on the account and can take SMS.
    /// Changes nothing, sends nothing.
    /// </summary>
    [HttpPost("test")]
    public async Task<ActionResult<SmsConnectionTestDto>> Test([FromServices] IElksApi api, CancellationToken ct)
    {
        var settings = await service.GetAsync();
        ElksLogin? login;
        try
        {
            login = await account.GetLoginAsync();
        }
        catch (ElksException ex)
        {
            return new SmsConnectionTestDto(false, [ex.Message]);
        }
        if (login is null)
            return new SmsConnectionTestDto(false, ["Inget 46elks-konto är inställt. Ange API-användarnamn och API-lösenord."]);

        List<ElksNumber> numbers;
        try
        {
            numbers = await api.GetNumbersAsync(login, ct);
        }
        catch (ElksException ex)
        {
            return new SmsConnectionTestDto(false, [ex.Message]);
        }

        var messages = new List<string> { "Inloggningen till 46elks fungerar." };
        if (settings.ElksNumber is null)
        {
            messages.Add("Ange 46elks-numret också.");
            return new SmsConnectionTestDto(false, messages);
        }

        var number = numbers.FirstOrDefault(n => Larmcentralen.Domain.Entities.SwedishPhoneNumber.SameNumber(n.Number, settings.ElksNumber));
        if (number is null)
        {
            var onAccount = numbers.Where(n => n.Active).Select(n => n.Number).ToList();
            messages.Add($"{settings.ElksNumber} finns inte på kontot."
                         + (onAccount.Count > 0 ? $" Kontot har {string.Join(", ", onAccount)}." : " Kontot har inga nummer."));
            return new SmsConnectionTestDto(false, messages);
        }
        if (!number.Active)
        {
            messages.Add($"{number.Number} är inte aktivt hos 46elks.");
            return new SmsConnectionTestDto(false, messages);
        }
        if (!number.Capabilities.Contains("sms"))
        {
            messages.Add($"{number.Number} kan inte ta emot SMS.");
            return new SmsConnectionTestDto(false, messages);
        }

        messages.Add($"{number.Number} finns på kontot och kan ta emot SMS.");
        if (!string.IsNullOrWhiteSpace(number.SmsUrl))
            messages.Add("Numret skickar också SMS vidare till en adress (sms_url) i 46elks. Det går bra: varje SMS hanteras en gång.");
        return new SmsConnectionTestDto(true, messages);
    }
}
