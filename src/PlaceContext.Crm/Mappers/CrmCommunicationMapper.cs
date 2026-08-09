using System.Text.Json;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

internal static class CrmCommunicationMapper
{
    public static CrmCommunicationView ToView(CrmCommunication communication) => new(
        communication.Id,
        communication.ClientId,
        communication.Channel.ToString(),
        communication.Subject,
        communication.Body,
        communication.Recipient,
        communication.Status.ToString(),
        communication.Provider,
        communication.Error,
        communication.CreatedByUserId,
        communication.CreatedAt,
        communication.SentAt);
}
