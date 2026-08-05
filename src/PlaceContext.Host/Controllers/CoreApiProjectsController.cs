using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Host.Api;
using PlaceContext.Host.Auth;
using PlaceContext.Host.CoreApi;

namespace PlaceContext.Host.Controllers;

[ApiController]
[Route("api/core/v1/projects")]
[Authorize(AuthenticationSchemes = CoreApiAuthenticationHandler.SchemeName)]
[Produces("application/json")]
public sealed class CoreApiProjectsController : ControllerBase
{
    private readonly IPlaceContextService _svc;
    private readonly ICoreApiResourceResolver _resource;

    public CoreApiProjectsController(IPlaceContextService svc, ICoreApiResourceResolver resource)
    {
        _svc = svc;
        _resource = resource;
    }

    [HttpGet]
    [Authorize(Policy = CoreApiScopes.ProjectsRead)]
    public async Task<ActionResult<IReadOnlyList<CoreProjectResponse>>> ListProjects()
    {
        var projects = await _svc.GetProjectsAsync(HttpContext.RequestAborted);
        return Ok(projects.Select(CoreApiMapper.ToResponse).ToList());
    }

    [HttpGet("{projectId:guid}")]
    [Authorize(Policy = CoreApiScopes.ProjectsRead)]
    public async Task<ActionResult<CoreProjectResponse>> GetProject(Guid projectId)
    {
        var project = await _resource.GetProjectAsync(projectId, HttpContext.RequestAborted);
        if (project is null)
            return NotFound(new { error = "Project not found." });

        return Ok(CoreApiMapper.ToResponse(project));
    }

    [HttpPost]
    [Authorize(Policy = CoreApiScopes.ProjectsWrite)]
    public async Task<ActionResult<CoreProjectResponse>> CreateProject([FromBody] CoreCreateProjectRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
            return BadRequest(new { error = "path is required." });

        var project = await _svc.CreateProjectAsync(request.Path, request.Name, HttpContext.RequestAborted);
        return CreatedAtAction(nameof(GetProject), new { projectId = project.Id }, CoreApiMapper.ToResponse(project));
    }
}
