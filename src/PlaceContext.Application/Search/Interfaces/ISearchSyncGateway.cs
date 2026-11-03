using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Search;

/// <summary>Triggers the external collector without coupling the application to its transport.</summary>
public interface ISearchSyncGateway
{
    Task<SearchSyncView> TriggerAsync(CancellationToken ct = default);
}
