using Larmcentralen.Api.Security;
using Microsoft.AspNetCore.Mvc;

namespace Larmcentralen.Api.Controllers;

/// <summary>What the caller's key allows. The app uses it to show or hide edit buttons.</summary>
[ApiController]
[Route("api/[controller]")]
public class MeController : ControllerBase
{
    [HttpGet]
    public ActionResult<AccessDto> Get()
    {
        var isAdmin = User.IsInRole(Roles.Admin);
        return Ok(new AccessDto(isAdmin ? Roles.Admin : Roles.User, isAdmin));
    }
}

public record AccessDto(string Role, bool CanEdit);
