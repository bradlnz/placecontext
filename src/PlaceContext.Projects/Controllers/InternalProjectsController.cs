using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;

namespace PlaceContext.Projects.Controllers;

[ApiController]
[Route("api/projects/internal")]
[Authorize(AuthenticationSchemes = "ApiKey")]
public sealed class InternalProjectsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await dispatcher.Query(new GetProjectsQuery(), ct));

    [HttpGet("{projectId:guid}/exists")]
    public async Task<IActionResult> Exists(Guid projectId, CancellationToken ct)
        => await dispatcher.Query(new GetProjectByIdQuery(projectId), ct) is null
            ? NotFound()
            : NoContent();
}
