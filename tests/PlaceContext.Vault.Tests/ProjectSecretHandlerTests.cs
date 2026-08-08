using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.TestSupport;
using PlaceContext.Vault.Domain.Repositories;

namespace PlaceContext.Vault.Tests;

public sealed class ProjectSecretHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Add_list_and_delete_use_protected_storage()
    {
        var projectId = Guid.NewGuid();
        var repository = new SecretRepository();
        var unitOfWork = new RecordingVaultUnitOfWork();
        var add = new AddProjectSecretHandler(
            repository, new PrefixSecretProtector(), unitOfWork, new FakeClock(Now));

        var created = await add.HandleAsync(new AddProjectSecretCommand(projectId, " API_KEY ", "secret"));

        Assert.Equal("API_KEY", created.Name);
        Assert.Equal("protected:secret", repository.Ciphers["API_KEY"]);
        Assert.Equal(1, unitOfWork.SaveCount);

        var listed = await new ListProjectSecretsHandler(repository)
            .HandleAsync(new ListProjectSecretsQuery(projectId));
        Assert.Equal("API_KEY", Assert.Single(listed).Name);

        await new DeleteProjectSecretHandler(repository, unitOfWork)
            .HandleAsync(new DeleteProjectSecretCommand(projectId, "API_KEY"));
        Assert.Empty(repository.Ciphers);
        Assert.Equal(2, unitOfWork.SaveCount);
    }

    private sealed class PrefixSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) => "protected:" + plaintext;
        public string Unprotect(string ciphertext) => ciphertext["protected:".Length..];
    }

    private sealed class SecretRepository : IProjectSecretRepository
    {
        private readonly Dictionary<string, DateTimeOffset> _created = new(StringComparer.Ordinal);
        public Dictionary<string, string> Ciphers { get; } = new(StringComparer.Ordinal);

        public Task<IReadOnlyList<(string Name, DateTimeOffset CreatedAt)>> ListAsync(
            Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<(string, DateTimeOffset)>>(
                _created.Select(entry => (entry.Key, entry.Value)).ToList());

        public Task AddAsync(
            Guid projectId, string name, string cipher, DateTimeOffset now, CancellationToken ct = default)
        {
            Ciphers.Add(name, cipher);
            _created.Add(name, now);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid projectId, string name, CancellationToken ct = default)
        {
            Ciphers.Remove(name);
            _created.Remove(name);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, string>> GetCiphersAsync(
            Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(Ciphers);
    }
}
