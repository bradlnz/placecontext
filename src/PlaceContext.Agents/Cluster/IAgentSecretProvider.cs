namespace PlaceContext.Agents.Cluster;

public interface IAgentSecretProvider
{
    Task<IReadOnlyDictionary<string, string>> GetSecretsAsync(
        Guid projectId,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken = default);
}
