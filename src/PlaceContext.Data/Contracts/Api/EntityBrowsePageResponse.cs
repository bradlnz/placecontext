using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;

namespace PlaceContext.Data.Contracts.Api;

public sealed record EntityBrowsePageResponse(
    DataEntityView Entity, IReadOnlyList<ProjectColumnInfo> Columns, ProjectTablePageResult Page);
