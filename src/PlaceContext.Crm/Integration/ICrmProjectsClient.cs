namespace PlaceContext.Crm.Integration;

public interface ICrmProjectsClient
{
    Task<bool> ExistsAsync(Guid projectId, CancellationToken ct = default);
}
