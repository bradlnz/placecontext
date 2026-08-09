using PlaceContext.Application.Ports;

namespace PlaceContext.Data.Integration;

public sealed record ReplaceDataSearchIndexRequest(
    string IndexName,
    IReadOnlyList<DataSearchMappingField> MappingFields,
    IReadOnlyList<string> ColumnNames,
    IReadOnlyList<IReadOnlyList<string?>> Rows,
    IReadOnlyList<string> JsonColumnNames);
