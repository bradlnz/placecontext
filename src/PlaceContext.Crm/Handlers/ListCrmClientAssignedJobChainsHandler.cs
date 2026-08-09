using System.Text.Json;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ListCrmClientAssignedJobChainsHandler
    : IQueryHandler<ListCrmClientAssignedJobChainsQuery, IReadOnlyList<Guid>>
{
    private readonly ICrmClientRepository _clients;
    private readonly ICrmClientJobChainAssignmentRepository _assignments;

    public ListCrmClientAssignedJobChainsHandler(
        ICrmClientRepository clients,
        ICrmClientJobChainAssignmentRepository assignments)
        => (_clients, _assignments) = (clients, assignments);

    public async Task<IReadOnlyList<Guid>> HandleAsync(
        ListCrmClientAssignedJobChainsQuery query,
        CancellationToken ct = default)
    {
        var client = await _clients.GetByIdAsync(query.ClientId, ct)
            ?? throw new InvalidOperationException($"Client {query.ClientId} not found.");
        if (client.ProjectId != query.ProjectId)
            throw new InvalidOperationException("The client and project do not match.");
        return await _assignments.ListForClientAsync(query.ProjectId, query.ClientId, ct);
    }
}
