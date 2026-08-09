namespace PlaceContext.Settings.Integration;

public interface ISettingsConnectionsClient
{
    Task<IReadOnlyList<SettingsProject>> ListProjectsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListSecretNamesAsync(Guid projectId, CancellationToken ct = default);
    Task SetSecretAsync(Guid projectId, string name, string value, CancellationToken ct = default);
    Task DeleteSecretAsync(Guid projectId, string name, CancellationToken ct = default);
}
