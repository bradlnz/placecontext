namespace PlaceContext.Application.DataSources;

public interface IDataFeedAccess
{
    Task<bool> AuthorizeAsync(Guid projectId, Guid sourceId, string token, CancellationToken ct);
}
