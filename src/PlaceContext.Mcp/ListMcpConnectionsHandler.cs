using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Mcp;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Mcp;

public sealed class ListMcpConnectionsHandler(IMcpConnectionRepository repository)
    : IQueryHandler<ListMcpConnectionsQuery, IReadOnlyList<McpConnectionView>>
{
    public async Task<IReadOnlyList<McpConnectionView>> HandleAsync(
        ListMcpConnectionsQuery query,
        CancellationToken cancellationToken = default)
    {
        var connections = await repository.ListByProjectAsync(query.ProjectId, cancellationToken);
        return connections.Select(McpConnectionMapper.ToView).ToList();
    }
}
