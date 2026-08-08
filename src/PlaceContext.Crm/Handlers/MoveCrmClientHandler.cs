using System.Text.Json;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class MoveCrmClientHandler : ICommandHandler<MoveCrmClientCommand, CrmClientView>
{
    private readonly ICrmClientRepository _clients;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly CrmAutomationDispatcher? _automations;

    public MoveCrmClientHandler(
        ICrmClientRepository clients, IUnitOfWork uow, IClock clock,
        CrmAutomationDispatcher? automations = null)
        => (_clients, _uow, _clock, _automations) = (clients, uow, clock, automations);

    public async Task<CrmClientView> HandleAsync(MoveCrmClientCommand command, CancellationToken ct = default)
    {
        var client = await _clients.GetByIdAsync(command.ClientId, ct)
            ?? throw new InvalidOperationException($"Client {command.ClientId} not found.");
        var previousStage = client.LifecycleStage;
        client.MoveTo(command.LifecycleStage, _clock.UtcNow);
        await _clients.UpdateAsync(client, ct);
        if (_automations is not null && previousStage != client.LifecycleStage)
            await _automations.EnqueueAsync(
                client, Domain.ValueObjects.CrmAutomationEventType.StageEntered, ct);
        await _uow.SaveChangesAsync(ct);
        return CrmClientMapper.ToView(client);
    }
}
