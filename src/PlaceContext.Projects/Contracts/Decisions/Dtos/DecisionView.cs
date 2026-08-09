namespace PlaceContext.Application.Dtos;

public sealed record DecisionView(
    Guid Id,
    string Question,
    string Choice,
    string Rationale,
    DateTimeOffset DecidedAt);
