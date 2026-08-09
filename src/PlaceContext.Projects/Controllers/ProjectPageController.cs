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
[Route("api/v1/projects/{projectId:guid}")]
[Authorize(
    AuthenticationSchemes = ProjectsAuthenticationDefaults.ApiKeyScheme,
    Policy = Permission.ProjectsView)]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ProjectPageController(
    IDispatcher dispatcher,
    ICurrentTenant currentTenant) : ControllerBase
{
    [HttpGet("overview-context")]
    public async Task<ActionResult<ProjectPageResponse>> Get(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var overview = await dispatcher.Query(
            new GetProjectOverviewQuery(projectId),
            cancellationToken);
        var timelineTask = LoadOptionalAsync(
            () => dispatcher.Query(new GetTimelineQuery(projectId, 8), cancellationToken),
            "Could not load timeline");
        var requirementsTask = LoadOptionalAsync(
            () => dispatcher.Query(new GetProjectRequirementsQuery(projectId), cancellationToken),
            "Could not load requirements");
        var decisionsTask = LoadOptionalAsync(
            () => dispatcher.Query(new GetDecisionsQuery(projectId), cancellationToken),
            "Could not load decisions");
        await Task.WhenAll(timelineTask, requirementsTask, decisionsTask);

        var timeline = await timelineTask;
        var requirements = await requirementsTask;
        var decisions = await decisionsTask;
        var message = timeline.Error ?? requirements.Error ?? decisions.Error;

        return Ok(new ProjectPageResponse(
            ProjectPageApiMapper.ToResponse(overview),
            timeline.Value is null ? null : ProjectPageApiMapper.ToResponse(timeline.Value),
            decisions.Value?.Select(decision =>
                ProjectPageApiMapper.ToResponse(decision, currentTenant.TimeZoneId)).ToList(),
            requirements.Value is null
                ? null
                : ProjectPageApiMapper.ToResponse(requirements.Value, currentTenant.TimeZoneId),
            message));
    }

    [HttpPut("requirements")]
    public async Task<ActionResult<ProjectPageRequirementsResponse>> UpdateRequirements(
        Guid projectId,
        [FromBody] UpdateProjectRequirementsRequest request,
        CancellationToken cancellationToken)
    {
        var requirements = await dispatcher.Send(
            new SetProjectRequirementsCommand(projectId, request.Markdown ?? string.Empty),
            cancellationToken);
        return Ok(ProjectPageApiMapper.ToResponse(requirements, currentTenant.TimeZoneId));
    }

    private static async Task<OptionalLoad<T>> LoadOptionalAsync<T>(
        Func<Task<T>> loader,
        string errorPrefix)
        where T : class
    {
        try
        {
            return new OptionalLoad<T>(await loader(), null);
        }
        catch (Exception exception)
        {
            return new OptionalLoad<T>(null, $"{errorPrefix}: {exception.Message}");
        }
    }
}
