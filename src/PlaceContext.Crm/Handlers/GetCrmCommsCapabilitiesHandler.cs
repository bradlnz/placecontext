using System.Text.Json;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Crm.Integration;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class GetCrmCommsCapabilitiesHandler
    : IQueryHandler<GetCrmCommsCapabilitiesQuery, CrmCommsCapabilitiesView>
{
    private readonly ICrmCommunicationsClient _sender;

    public GetCrmCommsCapabilitiesHandler(ICrmCommunicationsClient sender) => _sender = sender;

    public async Task<CrmCommsCapabilitiesView> HandleAsync(
        GetCrmCommsCapabilitiesQuery query,
        CancellationToken ct = default)
    {
        var value = await _sender.GetCapabilitiesAsync(ct);
        return new CrmCommsCapabilitiesView(
            value.EmailEnabled, value.SmsEnabled, value.EmailProvider, value.SmsProvider);
    }
}
