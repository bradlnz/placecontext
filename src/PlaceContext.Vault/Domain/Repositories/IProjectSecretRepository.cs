namespace PlaceContext.Vault.Domain.Repositories;

/// <summary>
/// Stores a project's encrypted secrets (the vault). Values are stored as ciphertext only.
/// Secrets are immutable once created — to change one, delete and recreate it.
/// </summary>
public interface IProjectSecretRepository
{
    /// <summary>Secret names + creation time (never values), for the portal list.</summary>
    Task<IReadOnlyList<(string Name, DateTimeOffset CreatedAt)>> ListAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>Insert a new secret. Throws if a secret with the same name already exists.</summary>
    Task AddAsync(Guid projectId, string name, string cipher, DateTimeOffset now, CancellationToken ct = default);

    Task DeleteAsync(Guid projectId, string name, CancellationToken ct = default);

    /// <summary>All of a project's secrets as name → ciphertext, for run-time decryption + injection.</summary>
    Task<IReadOnlyDictionary<string, string>> GetCiphersAsync(Guid projectId, CancellationToken ct = default);
}
