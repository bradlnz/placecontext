namespace PlaceContext.Application.Dtos;

/// <summary>Read model: token/cost totals for one project (root rollup).</summary>
public sealed record ProjectCostView(Guid ProjectId, string Project, long TotalTokens, decimal CostUsd);
