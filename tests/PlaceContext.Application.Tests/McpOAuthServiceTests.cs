using PlaceContext.Application.Mcp;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.TestSupport;

namespace PlaceContext.Application.Tests;

public sealed class McpOAuthServiceTests
{
    [Fact]
    public async Task Refresh_returns_current_token_and_preserves_refresh_token_when_provider_omits_it()
    {
        var connection = Connection();
        connection.StoreOAuthTokens("old", "refresh", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow);
        var repo = new Repository(connection);
        var uow = new RecordingUnitOfWork();
        var client = new Client();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var service = new McpOAuthService(repo, uow, new FakeDataEncryptor(), client, clock);

        var token = await service.GetTokenAsync(connection.Id, default);

        Assert.Equal("new", token!.AccessToken);
        Assert.Equal(clock.UtcNow.AddSeconds(3600), token.ExpiresAt);
        Assert.Equal("refresh", connection.OAuthRefreshToken);
        Assert.Equal("new", connection.OAuthAccessToken);
        Assert.Equal("oauth:connected", connection.LastStatus);
        Assert.Equal(1, uow.SaveCount);
        Assert.Equal(1, repo.Updates);
    }

    [Fact]
    public async Task Missing_connection_does_not_exchange_or_commit()
    {
        var uow = new RecordingUnitOfWork();
        var client = new Client();
        var service = new McpOAuthService(new Repository(null), uow, new FakeDataEncryptor(), client, new FakeClock(DateTimeOffset.UtcNow));
        Assert.Null(await service.CompleteAsync(Guid.NewGuid(), "https://provider/token", "code", "https://app/callback", "client", "verifier", default));
        Assert.Equal(0, client.Calls);
        Assert.Equal(0, uow.SaveCount);
    }

    [Fact]
    public async Task Failed_refresh_records_failure_without_replacing_saved_tokens()
    {
        var connection = Connection();
        connection.StoreOAuthTokens("old", "refresh", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow);
        var uow = new RecordingUnitOfWork();
        var service = new McpOAuthService(new Repository(connection), uow, new FakeDataEncryptor(), new Client { Fail = true }, new FakeClock(DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RefreshAsync(connection.Id, default));
        Assert.Equal("old", connection.OAuthAccessToken);
        Assert.Equal("refresh", connection.OAuthRefreshToken);
        Assert.Equal("oauth:expired", connection.LastStatus);
        Assert.Equal(1, uow.SaveCount);
    }

    private static McpConnection Connection() => McpConnection.Create(Guid.NewGuid(), "Test", "http", "https://provider/mcp", null, null, "oauth", null, null, DateTimeOffset.UtcNow);

    private sealed class Repository(McpConnection? connection) : IMcpConnectionRepository
    {
        public int Updates
        {
            get; private set;
        }
        public Task<McpConnection?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(connection?.Id == id ? connection : null);
        public Task UpdateAsync(McpConnection value, CancellationToken ct = default)
        {
            Updates++;
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<McpConnection>> ListByProjectAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(McpConnection value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class Client : IMcpOAuthClient
    {
        public int Calls
        {
            get; private set;
        }
        public bool Fail
        {
            get; init;
        }
        public Task<OAuthMetadata?> DiscoverMetadataAsync(string endpointUrl, CancellationToken ct) => Task.FromResult<OAuthMetadata?>(new() { TokenEndpoint = "https://provider/token" });
        public Task<string> RegisterClientAsync(string endpoint, string callback, CancellationToken ct) => throw new NotSupportedException();
        public Task<TokenResponse> ExchangeCodeAsync(string endpoint, string code, string redirect, string client, string verifier, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new TokenResponse { AccessToken = "new", ExpiresIn = 3600 });
        }
        public Task<TokenResponse> RefreshTokenAsync(string endpoint, string refresh, string client, CancellationToken ct)
        {
            Calls++;
            Assert.Equal("refresh", refresh);
            if (Fail)
            {
                throw new InvalidOperationException("Token rejected.");
            }
            return Task.FromResult(new TokenResponse { AccessToken = "new", ExpiresIn = 3600 });
        }
    }
}
