using PlaceContext.Application.Dtos;
using PlaceContext.Infrastructure.Comms;

namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record CommunicationsSettingsResponse(
    IReadOnlyList<CommunicationProviderView> Providers,
    IReadOnlyList<ProjectSummaryView> Projects);
