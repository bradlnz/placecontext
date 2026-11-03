namespace PlaceContext.Application.Dtos;

public sealed record SearchExportView(
    IReadOnlyList<SearchExportField> Fields,
    IReadOnlyList<IReadOnlyList<string?>> Rows,
    bool Truncated);
