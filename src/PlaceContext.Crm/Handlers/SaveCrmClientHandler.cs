using System.Text.Json;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Crm.Automation;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class SaveCrmClientHandler : ICommandHandler<SaveCrmClientCommand, CrmClientView>
{
    private readonly ICrmClientRepository _clients;
    private readonly ICrmUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly CrmAutomationDispatcher? _automations;

    public SaveCrmClientHandler(
        ICrmClientRepository clients, ICrmUnitOfWork uow, IClock clock,
        CrmAutomationDispatcher? automations = null)
        => (_clients, _uow, _clock, _automations) = (clients, uow, clock, automations);

    public async Task<CrmClientView> HandleAsync(SaveCrmClientCommand command, CancellationToken ct = default)
    {
        CrmClient client;
        if (command.ClientId is { } id)
        {
            client = await _clients.GetByIdAsync(id, ct)
                ?? throw new InvalidOperationException($"Client {id} not found.");
            if (client.ProjectId != command.ProjectId)
                throw new InvalidOperationException("Client does not belong to this project.");
            var previousStage = client.LifecycleStage;
            client.Update(command.Name, command.Company, command.Email, command.Phone,
                command.LifecycleStage, command.Notes, _clock.UtcNow);
            await _clients.UpdateAsync(client, ct);
            if (_automations is not null)
            {
                await _automations.EnqueueAsync(client, Domain.ValueObjects.CrmAutomationEventType.ClientUpdated, ct);
                if (previousStage != client.LifecycleStage)
                    await _automations.EnqueueAsync(client, Domain.ValueObjects.CrmAutomationEventType.StageEntered, ct);
            }
        }
        else
        {
            client = CrmClient.Create(command.ProjectId, command.Name, command.Company, command.Email,
                command.Phone, command.LifecycleStage, command.Notes, _clock.UtcNow);
            await _clients.AddAsync(client, ct);
            if (_automations is not null)
            {
                await _automations.EnqueueAsync(client, Domain.ValueObjects.CrmAutomationEventType.ClientCreated, ct);
                await _automations.EnqueueAsync(client, Domain.ValueObjects.CrmAutomationEventType.StageEntered, ct);
            }
        }

        await _uow.SaveChangesAsync(ct);
        return CrmClientMapper.ToView(client);
    }
}
