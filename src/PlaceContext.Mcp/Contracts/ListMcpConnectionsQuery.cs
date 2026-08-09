using System.Text.Json;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

public sealed record ListMcpConnectionsQuery(Guid ProjectId)
    : IQuery<IReadOnlyList<McpConnectionView>>;
