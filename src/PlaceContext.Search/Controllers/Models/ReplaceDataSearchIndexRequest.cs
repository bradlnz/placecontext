using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;

namespace PlaceContext.Search.Controllers;

public sealed record ReplaceDataSearchIndexRequest(
    string IndexName,
    IReadOnlyList<DataSearchMappingField> MappingFields,
    IReadOnlyList<string> ColumnNames,
    IReadOnlyList<IReadOnlyList<string?>> Rows,
    IReadOnlyList<string> JsonColumnNames);
