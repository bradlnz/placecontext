namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record ProjectPageChangeResponse(
    Guid Id,
    int Sequence,
    string Title,
    string Kind,
    string? Commit);
