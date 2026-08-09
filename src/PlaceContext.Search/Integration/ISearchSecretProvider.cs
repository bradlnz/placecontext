namespace PlaceContext.Search.Integration;

public interface ISearchSecretProvider
{
    Task<IReadOnlyDictionary<string, string>> GetSecretsAsync(
        Guid projectId,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken = default);
}
