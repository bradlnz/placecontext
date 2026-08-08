using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Ports;

/// <summary>Triggers the external collector without coupling the application to its transport.</summary>
public interface IOpenSearchSyncGateway
{
    Task<OpenSearchSyncView> TriggerAsync(CancellationToken ct = default);
}
