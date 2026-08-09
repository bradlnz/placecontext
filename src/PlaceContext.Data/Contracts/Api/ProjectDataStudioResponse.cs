using PlaceContext.Application.Ports;

namespace PlaceContext.Data.Contracts.Api;

public sealed record ProjectDataStudioResponse(
    IReadOnlyList<ProjectTableInfo> Tables,
    IReadOnlyList<DataStudioIndexResponse> Indices,
    IReadOnlyList<SavedQueryRecord> SavedQueries);
