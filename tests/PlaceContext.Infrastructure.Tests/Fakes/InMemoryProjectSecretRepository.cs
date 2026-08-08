using PlaceContext.Vault.Domain.Repositories;

namespace PlaceContext.Infrastructure.Tests;

internal sealed class InMemoryProjectSecretRepository : IProjectSecretRepository
{
    private readonly Dictionary<Guid, Dictionary<string, (string Cipher, DateTimeOffset CreatedAt)>> _secrets = new();

    public Task<IReadOnlyList<(string Name, DateTimeOffset CreatedAt)>> ListAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<(string Name, DateTimeOffset CreatedAt)> result = _secrets
            .GetValueOrDefault(projectId)?
            .Select(secret => (secret.Key, secret.Value.CreatedAt))
            .OrderBy(secret => secret.Key, StringComparer.Ordinal)
            .ToList()
            ?? [];
        return Task.FromResult(result);
    }

    public Task AddAsync(
        Guid projectId,
        string name,
        string cipher,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!_secrets.TryGetValue(projectId, out var projectSecrets))
        {
            projectSecrets = new Dictionary<string, (string Cipher, DateTimeOffset CreatedAt)>(
                StringComparer.Ordinal);
            _secrets.Add(projectId, projectSecrets);
        }

        if (!projectSecrets.TryAdd(name, (cipher, now)))
            throw new InvalidOperationException($"Secret '{name}' already exists.");

        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid projectId, string name, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _secrets.GetValueOrDefault(projectId)?.Remove(name);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, string>> GetCiphersAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyDictionary<string, string> result = _secrets
            .GetValueOrDefault(projectId)?
            .ToDictionary(secret => secret.Key, secret => secret.Value.Cipher, StringComparer.Ordinal)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
        return Task.FromResult(result);
    }
}
