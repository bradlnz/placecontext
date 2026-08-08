namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record ProjectPageDecisionResponse(
    Guid Id,
    string Question,
    string Choice,
    string Rationale,
    DateTimeOffset DecidedAt,
    string DecidedAtDisplay);
