namespace PlaceContext.Application.Dtos;

/// <summary>Read model: token/cost totals for one model family.</summary>
public sealed record ModelCostView(string Model, long InputTokens, long OutputTokens, decimal CostUsd);
