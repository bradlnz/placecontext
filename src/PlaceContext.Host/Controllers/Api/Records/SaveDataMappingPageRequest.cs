using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record SaveDataMappingPageRequest(
    Guid? Id,
    Guid JobId,
    string TargetTable,
    string? RowsPath,
    IReadOnlyList<DataFieldDto> Fields,
    bool Enabled);
