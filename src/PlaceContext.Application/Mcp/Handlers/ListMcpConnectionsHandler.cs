using System.Net.Http;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Mcp;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ListMcpConnectionsHandler : IQueryHandler<ListMcpConnectionsQuery, IReadOnlyList<McpConnectionView>>
{
    private readonly IMcpConnectionRepository _repo;

    public ListMcpConnectionsHandler(IMcpConnectionRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<McpConnectionView>> HandleAsync(ListMcpConnectionsQuery query, CancellationToken ct = default)
    {
        var connections = await _repo.ListByProjectAsync(query.ProjectId, ct);
        return connections.Select(McpConnectionMapper.ToView).ToList();
    }
}
