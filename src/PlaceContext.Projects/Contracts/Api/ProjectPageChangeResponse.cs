namespace PlaceContext.Projects.Api;

public sealed record ProjectPageChangeResponse(
    Guid Id,
    int Sequence,
    string Title,
    string Kind,
    string? Commit);
