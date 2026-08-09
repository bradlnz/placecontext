namespace PlaceContext.Data.Integration;

public interface IDataVaultClient
{
    Task<IReadOnlyDictionary<string, string>> GetSecretsAsync(
        Guid projectId,
        IReadOnlyList<string> names,
        CancellationToken ct = default);
}
