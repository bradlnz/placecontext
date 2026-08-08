using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record EntityBrowsePageResponse(
    DataEntityView Entity,
    IReadOnlyList<ProjectColumnInfo> Columns,
    ProjectTablePageResult Page);
