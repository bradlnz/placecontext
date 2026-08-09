namespace PlaceContext.Communications;

public interface ICommunicationVaultClient
{
    Task<string?> ResolveAsync(Guid projectId, string name, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid projectId, string name, CancellationToken ct = default);
}
