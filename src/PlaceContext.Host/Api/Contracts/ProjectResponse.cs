using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Api;

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
