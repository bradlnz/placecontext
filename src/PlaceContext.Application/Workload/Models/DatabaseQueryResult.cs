namespace PlaceContext.Application.Workload;

public sealed record DatabaseQueryResult(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string?>> Rows, bool Truncated = false, string? Cursor = null);
