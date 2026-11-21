using Larmcentralen.Application.DTOs;
using Larmcentralen.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Larmcentralen.Api.Controllers;

/// <summary>
/// Feeds the client's offline copy. Returns every area, equipment, alarm and solution in one call.
/// The knowledge base is a few hundred alarms, so a full snapshot is simpler and safer than delta sync.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SyncController(ISyncService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SyncSnapshotDto>> Get()
        => Ok(await service.GetSnapshotAsync());
}
