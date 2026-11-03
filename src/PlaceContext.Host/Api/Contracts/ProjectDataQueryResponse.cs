namespace PlaceContext.Host.Api;

public sealed record ProjectDataQueryResponse(
    IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string?>> Rows, bool Truncated);
