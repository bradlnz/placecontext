namespace PlaceContext.Application.Dtos;

/// <summary>Read model: tenant-wide cost analysis across all projects.</summary>
public sealed record RootCostView(
    long InputTokens,
    long OutputTokens,
    decimal CostUsd,
    int RecordCount,
    IReadOnlyList<ModelCostView> ByModel,
    IReadOnlyList<ProjectCostView> ByProject);
