using System.Globalization;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Projects.Api;

internal static class ProjectPageApiMapper
{
    public static ProjectPageOverviewResponse ToResponse(ProjectOverviewView overview) => new(
        overview.Id,
        overview.Name,
        overview.Path,
        overview.Status,
        overview.GodNodes.Select(node => new ProjectPageGodNodeResponse(
            node.Id,
            node.Label,
            node.Degree)).ToList());

    public static ProjectPageTimelineResponse ToResponse(ActivityTimelineView timeline) => new(
        timeline.Changes.Select(change => new ProjectPageChangeResponse(
            change.Id,
            change.Sequence,
            change.Title,
            change.Kind,
            change.Commit)).ToList());

    public static ProjectPageDecisionResponse ToResponse(
        DecisionView decision,
        string timeZoneId) => new(
            decision.Id,
            decision.Question,
            decision.Choice,
            decision.Rationale,
            decision.DecidedAt,
            ToWorkspaceTime(decision.DecidedAt, timeZoneId)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    public static ProjectPageRequirementsResponse ToResponse(
        RequirementsView requirements,
        string timeZoneId) => new(
            requirements.Markdown,
            requirements.UpdatedAt,
            requirements.UpdatedAt is { } updatedAt
                ? ToWorkspaceTime(updatedAt, timeZoneId)
                    .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                : null);

    private static DateTimeOffset ToWorkspaceTime(DateTimeOffset value, string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.ConvertTime(value, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
        }
        catch
        {
            return value;
        }
    }
}
