namespace PlaceContext.Crm.Integration;

public interface ICrmArtifactsClient
{
    Task<IReadOnlyList<CrmRunArtifactSummary>> ListForRunAsync(
        Guid runId,
        CancellationToken ct = default);

    Task<CrmStoredObject> StoreAsync(
        Guid projectId,
        Guid clientId,
        Guid objectId,
        byte[] content,
        string contentType,
        CancellationToken ct = default);

    Task<CrmArtifactContent?> ReadAsync(
        string bucket,
        string objectKey,
        CancellationToken ct = default);

    Task DeleteAsync(
        string bucket,
        string objectKey,
        CancellationToken ct = default);
}
