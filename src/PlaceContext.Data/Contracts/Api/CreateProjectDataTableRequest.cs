using PlaceContext.Application.Ports;

namespace PlaceContext.Data.Contracts.Api;

public sealed record CreateProjectDataTableRequest(
    string Name,
    IReadOnlyList<ProjectColumnSpec> Columns);
