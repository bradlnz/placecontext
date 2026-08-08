using System.Text.Json;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class SetCrmClientAssignedJobChainsHandler
    : ICommandHandler<SetCrmClientAssignedJobChainsCommand, IReadOnlyList<Guid>>
{
    private readonly ICrmClientRepository _clients;
    private readonly ICrmClientJobChainAssignmentRepository _assignments;
    private readonly IJobChainRepository _chains;
    private readonly ICrmUnitOfWork _uow;

    public SetCrmClientAssignedJobChainsHandler(
        ICrmClientRepository clients,
        ICrmClientJobChainAssignmentRepository assignments,
        IJobChainRepository chains,
        ICrmUnitOfWork uow)
        => (_clients, _assignments, _chains, _uow) = (clients, assignments, chains, uow);

    public async Task<IReadOnlyList<Guid>> HandleAsync(
        SetCrmClientAssignedJobChainsCommand command,
        CancellationToken ct = default)
    {
        var client = await _clients.GetByIdAsync(command.ClientId, ct)
            ?? throw new InvalidOperationException($"Client {command.ClientId} not found.");
        if (client.ProjectId != command.ProjectId)
            throw new InvalidOperationException("The client and project do not match.");

        var desired = command.ChainIds
            .Where(chainId => chainId != Guid.Empty)
            .Distinct()
            .ToArray();

        if (desired.Length > 0)
        {
            var chainIds = (await _chains.ListForProjectAsync(command.ProjectId, ct))
                .Select(chain => chain.Id)
                .ToHashSet();
            var invalidChainId = desired.FirstOrDefault(chainId => !chainIds.Contains(chainId));
            if (invalidChainId != Guid.Empty)
                throw new InvalidOperationException("One or more chains do not belong to the project.");
        }

        await _assignments.SetForClientAsync(command.ProjectId, command.ClientId, desired, ct);
        await _uow.SaveChangesAsync(ct);
        return desired;
    }
}
