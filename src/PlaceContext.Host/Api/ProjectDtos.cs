using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Api;

/// <summary>Request body for POST /api/v1/projects.</summary>
public sealed record CreateProjectRequest(string Path, string? Name);

/// <summary>
/// Public read model for a project — the management API's stable contract. Deliberately narrower than
/// the portal's <see cref="ProjectSummaryView"/> (which also carries graph-internal fields like
/// GodNodeCount/NodeCount/LinkCount that are implementation detail, not something a Terraform config
/// declares or reads back).
/// </summary>
public sealed record ProjectResponse(
    Guid Id,
    string Name,
    string Path,
    /// <summary>"Discovered" | "Registered" | "Graphified" | "Archived".</summary>
    string Status,
    bool IsGraphified);

/// <summary>Translates between the management API's project DTOs and the internal read model.</summary>
internal static class ProjectApiMapper
{
    public static ProjectResponse ToResponse(ProjectSummaryView v) => new(
        v.Id, v.Name, v.Path, v.Status, v.IsGraphified);
}
