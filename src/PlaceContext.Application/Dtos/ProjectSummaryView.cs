using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Dtos;

/// <summary>Read model: one row in the projects list.</summary>
public sealed record ProjectSummaryView(
    Guid Id,
    string Name,
    string Path,
    string Status,
    bool IsGraphified,
    int GodNodeCount,
    int NodeCount,
    int LinkCount,
    double? TechnicalRisk,
    string? TechnicalBand,
    double? ProcessRisk,
    string? ProcessBand);
