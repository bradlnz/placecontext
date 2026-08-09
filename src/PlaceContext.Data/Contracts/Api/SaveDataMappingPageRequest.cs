using PlaceContext.Application.Dtos;

namespace PlaceContext.Data.Contracts.Api;

public sealed record SaveDataMappingPageRequest(
    Guid? Id,
    Guid JobId,
    string TargetTable,
    string? RowsPath,
    IReadOnlyList<DataFieldDto> Fields,
    bool Enabled);
