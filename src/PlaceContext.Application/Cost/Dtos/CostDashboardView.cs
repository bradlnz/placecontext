namespace PlaceContext.Application.Dtos;

/// <summary>Read model: a project's cost dashboard.</summary>
public sealed record CostDashboardView(
    Guid ProjectId,
    long InputTokens,
    long OutputTokens,
    decimal CostUsd,
    IReadOnlyList<ModelCostView> ByModel,
    IReadOnlyList<UsageEntryView> Recent);
