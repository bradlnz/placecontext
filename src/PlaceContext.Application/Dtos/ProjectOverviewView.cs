using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Dtos;

/// <summary>Read model: the project overview page.</summary>
public sealed record ProjectOverviewView(
    Guid Id,
    string Name,
    string Path,
    string Status,
    DateTimeOffset? RegisteredAt,
    DateTimeOffset? GraphBuiltAt,
    int NodeCount,
    int LinkCount,
    IReadOnlyList<GodNodeView> GodNodes,
    RiskDashboardView Risk,
    int ChangeCount,
    string Context);
