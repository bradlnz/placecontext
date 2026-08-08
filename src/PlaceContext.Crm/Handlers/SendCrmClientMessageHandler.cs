using System.Text.Json;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class SendCrmClientMessageHandler
    : ICommandHandler<SendCrmClientMessageCommand, CrmCommunicationView>
{
    private readonly ICrmClientRepository _clients;
    private readonly ICrmCommunicationRepository _communications;
    private readonly IClientCommunicationSender _sender;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly CrmAutomationDispatcher? _automations;

    public SendCrmClientMessageHandler(
        ICrmClientRepository clients,
        ICrmCommunicationRepository communications,
        IClientCommunicationSender sender,
        ICurrentUser currentUser,
        IUnitOfWork uow,
        IClock clock,
        CrmAutomationDispatcher? automations = null)
        => (_clients, _communications, _sender, _currentUser, _uow, _clock, _automations)
            = (clients, communications, sender, currentUser, uow, clock, automations);

    public async Task<CrmCommunicationView> HandleAsync(
        SendCrmClientMessageCommand command,
        CancellationToken ct = default)
    {
        var client = await _clients.GetByIdAsync(command.ClientId, ct)
            ?? throw new InvalidOperationException($"Client {command.ClientId} not found.");
        var recipient = command.Channel switch
        {
            Domain.ValueObjects.CrmCommunicationChannel.Email => client.Email,
            Domain.ValueObjects.CrmCommunicationChannel.Sms => client.Phone,
            _ => throw new ArgumentException("Choose email or SMS.", nameof(command.Channel)),
        };
        var message = CrmCommunication.CreateOutbound(
            client.ProjectId, client.Id, command.Channel, command.Subject, command.Body,
            recipient ?? "", _currentUser.UserId, _clock.UtcNow);
        await _communications.AddAsync(message, ct);
        await _uow.SaveChangesAsync(ct);

        try
        {
            var delivery = command.Channel == Domain.ValueObjects.CrmCommunicationChannel.Email
                ? await _sender.SendEmailAsync(recipient!, client.Name, command.Subject!, command.Body, ct)
                : await _sender.SendSmsAsync(recipient!, command.Body, ct);
            message.MarkSent(delivery.Provider, delivery.ExternalId, _clock.UtcNow);
            if (_automations is not null)
                await _automations.EnqueueAsync(
                    client, Domain.ValueObjects.CrmAutomationEventType.CommunicationSent, ct);
        }
        catch (Exception ex)
        {
            var capabilities = await _sender.GetCapabilitiesAsync(ct);
            var provider = command.Channel == Domain.ValueObjects.CrmCommunicationChannel.Email
                ? capabilities.EmailProvider
                : capabilities.SmsProvider;
            message.MarkFailed(provider, ex.Message);
        }

        await _communications.UpdateAsync(message, ct);
        await _uow.SaveChangesAsync(ct);
        return CrmCommunicationMapper.ToView(message);
    }
}
