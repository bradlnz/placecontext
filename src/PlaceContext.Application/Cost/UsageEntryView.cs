namespace PlaceContext.Application.Dtos;

/// <summary>Read model: one row recorded into the usage ledger (with its computed cost).</summary>
public sealed record UsageEntryView(
    Guid Id, string Model, long InputTokens, long OutputTokens, decimal CostUsd, string? Description, DateTimeOffset At);
