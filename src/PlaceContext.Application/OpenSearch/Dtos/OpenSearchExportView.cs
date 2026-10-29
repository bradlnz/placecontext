namespace PlaceContext.Application.Dtos;

public sealed record OpenSearchExportView(
    IReadOnlyList<OpenSearchExportField> Fields,
    IReadOnlyList<IReadOnlyList<string?>> Rows,
    bool Truncated);
