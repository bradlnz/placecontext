using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record ProjectDataAdminResponse(
    IReadOnlyList<DataMappingView> Mappings,
    IReadOnlyList<DataAdminJobResponse> Jobs,
    IReadOnlyList<ProjectTableInfo> Tables,
    IReadOnlyList<DataEntityView> Entities,
    IReadOnlyList<RecordLinkGroup> LinkGroups);
