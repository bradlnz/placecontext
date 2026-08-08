using System.Globalization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Controllers.Api.Records;

namespace PlaceContext.Host.Controllers;

[ApiController]
[Route("api/v1/projects/{projectId:guid}")]
[Authorize(
    AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme,
    Policy = Permission.ProjectsView)]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ProjectPageController(IPlaceContextService placeContextService) : ControllerBase
{
    [HttpGet("overview-context")]
    public async Task<ActionResult<ProjectPageResponse>> Get(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var overview = await placeContextService.GetProjectOverviewAsync(projectId, cancellationToken);
        var timelineTask = LoadOptionalAsync(
            () => placeContextService.GetTimelineAsync(projectId, 8, cancellationToken),
            "Could not load timeline");
        var requirementsTask = LoadOptionalAsync(
            () => placeContextService.GetProjectRequirementsAsync(projectId, cancellationToken),
            "Could not load requirements");
        var decisionsTask = LoadOptionalAsync(
            () => placeContextService.GetDecisionsAsync(projectId, cancellationToken),
            "Could not load decisions");
        await Task.WhenAll(timelineTask, requirementsTask, decisionsTask);

        var timeline = await timelineTask;
        var requirements = await requirementsTask;
        var decisions = await decisionsTask;
        var message = timeline.Error ?? requirements.Error ?? decisions.Error;

        return Ok(new ProjectPageResponse(
            new ProjectPageOverviewResponse(
                overview.Id,
                overview.Name,
                overview.Path,
                overview.Status,
                overview.GodNodes.Select(node => new ProjectPageGodNodeResponse(
                    node.Id,
                    node.Label,
                    node.Degree)).ToList()),
            timeline.Value is null
                ? null
                : new ProjectPageTimelineResponse(timeline.Value.Changes.Select(change =>
                    new ProjectPageChangeResponse(
                        change.Id,
                        change.Sequence,
                        change.Title,
                        change.Kind,
                        change.Commit)).ToList()),
            decisions.Value?.Select(MapDecision).ToList(),
            requirements.Value is null ? null : MapRequirements(requirements.Value),
            message));
    }

    [HttpPut("requirements")]
    public async Task<ActionResult<ProjectPageRequirementsResponse>> UpdateRequirements(
        Guid projectId,
        [FromBody] UpdateProjectRequirementsRequest request,
        CancellationToken cancellationToken)
    {
        var requirements = await placeContextService.SetProjectRequirementsAsync(
            projectId,
            request.Markdown ?? string.Empty,
            cancellationToken);
        return Ok(MapRequirements(requirements));
    }

    private static ProjectPageDecisionResponse MapDecision(DecisionView decision) =>
        new(
            decision.Id,
            decision.Question,
            decision.Choice,
            decision.Rationale,
            decision.DecidedAt,
            decision.DecidedAt.ToWorkspaceTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    private static ProjectPageRequirementsResponse MapRequirements(RequirementsView requirements) =>
        new(
            requirements.Markdown,
            requirements.UpdatedAt,
            requirements.UpdatedAt?.ToWorkspaceTime().ToString(
                "yyyy-MM-dd HH:mm",
                CultureInfo.InvariantCulture));

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
