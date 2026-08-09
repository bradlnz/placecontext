using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Projects.Api;
using PlaceContext.Projects.Auth;

namespace PlaceContext.Projects.Controllers;

[ApiController]
[Route("api/v1/projects")]
[Authorize(AuthenticationSchemes = ProjectsAuthenticationDefaults.ApiKeyScheme)]
[Produces("application/json")]
public sealed class ManagementProjectsController(IDispatcher dispatcher) : ControllerBase
{
    public const string GetByIdRouteName = "GetProjectById";

    [HttpGet("{id:guid}", Name = GetByIdRouteName)]
    [Authorize(Policy = Permission.ProjectsView)]
    public async Task<ActionResult<ProjectResponse>> GetById(Guid id)
    {
        var project = await dispatcher.Query(
            new GetProjectByIdQuery(id),
            HttpContext.RequestAborted);
        return project is null
            ? NotFound()
            : Ok(ProjectApiMapper.ToResponse(project));
    }

    [HttpPost]
    [Authorize(Policy = Permission.ProjectsManage)]
    public async Task<ActionResult<ProjectResponse>> Create([FromBody] CreateProjectRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
            return BadRequest(new { error = "path is required." });

        var project = await dispatcher.Send(
            new CreateProjectCommand(request.Path, request.Name),
            HttpContext.RequestAborted);
        var response = ProjectApiMapper.ToResponse(project);
        return CreatedAtRoute(GetByIdRouteName, new { id = response.Id }, response);
    }

    [HttpGet]
    [Authorize(Policy = Permission.ProjectsView)]
    public async Task<ActionResult<IReadOnlyList<ProjectResponse>>> List()
    {
        var projects = await dispatcher.Query(
            new GetProjectsQuery(),
            HttpContext.RequestAborted);
        return Ok(projects.Select(ProjectApiMapper.ToResponse).ToList());
    }
}
