using System.Text.Json;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class GetCrmCommsCapabilitiesHandler
    : IQueryHandler<GetCrmCommsCapabilitiesQuery, CrmCommsCapabilitiesView>
{
    private readonly IClientCommunicationSender _sender;

    public GetCrmCommsCapabilitiesHandler(IClientCommunicationSender sender) => _sender = sender;

    public async Task<CrmCommsCapabilitiesView> HandleAsync(
        GetCrmCommsCapabilitiesQuery query,
        CancellationToken ct = default)
    {
        var value = await _sender.GetCapabilitiesAsync(ct);
        return new CrmCommsCapabilitiesView(
            value.EmailEnabled, value.SmsEnabled, value.EmailProvider, value.SmsProvider);
    }
}
