using Larmcentralen.Api.Security;
using Larmcentralen.Application.DTOs;
using Larmcentralen.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace Larmcentralen.Api.Controllers;

/// <summary>
/// Contact people operators can call. Admin only, reads included: these are names and phone numbers, and nothing
/// shows them to operators yet.
/// </summary>
[ApiController]
[Route("api/contacts")]
[Authorize(Policy = Policies.Edit)]
[ConcurrentChangeIsConflict]
public class ContactsController(IContactService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ContactDto>>> GetAll() => Ok(await service.GetContactsAsync());

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ContactDto>> Get(int id)
        => await service.GetContactAsync(id) is { } contact ? Ok(contact) : NotFound();

    /// <summary>400 (Swedish reason) without a name, role or a Swedish phone number.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(SaveContactDto dto)
    {
        var result = await service.CreateContactAsync(dto);
        return result.Status == WriteStatus.Ok
            ? CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value)
            : this.ToError(result.Status, result.Error);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SaveContactDto dto)
    {
        var result = await service.UpdateContactAsync(id, dto);
        return result.Status == WriteStatus.Ok ? Ok(result.Value) : this.ToError(result.Status, result.Error);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id) => await service.DeleteContactAsync(id) ? NoContent() : NotFound();
}

/// <summary>Roles for contact people ("Teamledare", "Underhåll el"). Admin only, reads included.</summary>
[ApiController]
[Route("api/contact-roles")]
[Authorize(Policy = Policies.Edit)]
[ConcurrentChangeIsConflict]
public class ContactRolesController(IContactService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ContactRoleDto>>> GetAll() => Ok(await service.GetRolesAsync());

    /// <summary>400 without a name, 409 when another role already has it (ignoring case).</summary>
    [HttpPost]
    public async Task<IActionResult> Create(SaveContactRoleDto dto)
    {
        var result = await service.CreateRoleAsync(dto);
        return result.Status == WriteStatus.Ok
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : this.ToError(result.Status, result.Error);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SaveContactRoleDto dto)
    {
        var result = await service.UpdateRoleAsync(id, dto);
        return result.Status == WriteStatus.Ok ? Ok(result.Value) : this.ToError(result.Status, result.Error);
    }

    /// <summary>409 while contacts have the role: the message names them, and <c>contacts</c> lists them.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await service.DeleteRoleAsync(id);
        if (result.Status == WriteStatus.Conflict)
        {
            var problem = new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "Rollen används", Detail = result.Error };
            problem.Extensions["contacts"] = result.Value;
            return Conflict(problem);
        }
        return result.Status == WriteStatus.Ok ? NoContent() : this.ToError(result.Status, result.Error);
    }
}

/// <summary>
/// Two admins at once can still trip the database's own rules after the service's checks passed (the same role name
/// added twice, or a role removed just as someone gives it to a contact). Answer 409 with a Swedish reason, not 500.
/// </summary>
public sealed class ConcurrentChangeIsConflictAttribute : Attribute, IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not DbUpdateException) return;
        context.Result = new ConflictObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Detail = "Någon annan ändrade samtidigt. Ladda om sidan och försök igen."
        });
        context.ExceptionHandled = true;
    }
}
