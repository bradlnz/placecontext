using System.Text.Json;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Crm.Automation;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class AddCrmClientNoteHandler
    : ICommandHandler<AddCrmClientNoteCommand, CrmCommunicationView>
{
    private readonly ICrmClientRepository _clients;
    private readonly ICrmCommunicationRepository _communications;
    private readonly ICurrentUser _currentUser;
    private readonly ICrmUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly CrmAutomationDispatcher? _automations;

    public AddCrmClientNoteHandler(
        ICrmClientRepository clients,
        ICrmCommunicationRepository communications,
        ICurrentUser currentUser,
        ICrmUnitOfWork uow,
        IClock clock,
        CrmAutomationDispatcher? automations = null)
        => (_clients, _communications, _currentUser, _uow, _clock, _automations)
            = (clients, communications, currentUser, uow, clock, automations);

    public async Task<CrmCommunicationView> HandleAsync(
        AddCrmClientNoteCommand command,
        CancellationToken ct = default)
    {
        var client = await _clients.GetByIdAsync(command.ClientId, ct)
            ?? throw new InvalidOperationException($"Client {command.ClientId} not found.");
        var note = CrmCommunication.CreateNote(
            client.ProjectId, client.Id, command.Body, _currentUser.UserId, _clock.UtcNow);
        await _communications.AddAsync(note, ct);
        if (_automations is not null)
            await _automations.EnqueueAsync(
                client, Domain.ValueObjects.CrmAutomationEventType.NoteAdded, ct);
        await _uow.SaveChangesAsync(ct);
        return CrmCommunicationMapper.ToView(note);
    }
}
