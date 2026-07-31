using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Comms;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests;

public sealed class PostmarkCommunicationTests
{
    [Fact]
    public async Task Vault_token_is_resolved_at_send_time_for_transactional_email()
    {
        var tenant = new FakeCurrentTenant(Guid.NewGuid());
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AppDbContext(dbOptions, tenant);
        var vaultProjectId = Guid.NewGuid();
        var secrets = new EfProjectSecretRepository(db);
        await secrets.AddAsync(
            vaultProjectId, "POSTMARK_SERVER_TOKEN", "server-token", DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        CapturedRequest? captured = null;
        var handler = new StubHandler(async request =>
        {
            captured = new CapturedRequest(
                request.Headers.ToDictionary(x => x.Key, x => string.Join(",", x.Value)),
                request.Content is null ? "" : await request.Content.ReadAsStringAsync());
            return Json("""
                {"ErrorCode":0,"Message":"OK","MessageID":"message-123","SubmittedAt":"2026-07-31T00:00:00Z","To":"ada@example.test"}
                """);
        });
        var factory = new StubHttpClientFactory(handler);
        var options = Options.Create(new ClientCommsOptions
        {
            Postmark = new PostmarkOptions { ApiEndpoint = "https://api.postmark.test" },
        });
        var connection = new PostmarkConnectionService(
            db, secrets, new PlaintextSecretProtector());
        await connection.SaveSettingsAsync(
            vaultProjectId, "POSTMARK_SERVER_TOKEN",
            "hello@example.test", "Example Team", "outbound");
        var status = await connection.GetStatusAsync();
        Assert.True(status.Configured);
        Assert.True(status.Ready);

        var sender = new SendGridTwilioCommunicationSender(factory, options, connection);
        var delivery = await sender.SendEmailAsync(
            "ada@example.test", "Ada Lovelace", "Welcome", "Hello Ada");

        Assert.Equal("Postmark", delivery.Provider);
        Assert.Equal("message-123", delivery.ExternalId);
        Assert.NotNull(captured);
        Assert.Equal("server-token", captured.Headers["X-Postmark-Server-Token"]);
        Assert.Contains("\"MessageStream\":\"outbound\"", captured.Body);
        Assert.Contains("\"Tag\":\"crm-transactional\"", captured.Body);
        Assert.Contains("\"From\":\"\\u0022Example Team\\u0022 \\u003Chello@example.test\\u003E\"", captured.Body);
    }

    [Fact]
    public async Task Capability_is_disabled_when_referenced_vault_secret_is_missing()
    {
        var tenant = new FakeCurrentTenant(Guid.NewGuid());
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AppDbContext(dbOptions, tenant);
        var secrets = new EfProjectSecretRepository(db);
        var connection = new PostmarkConnectionService(
            db, secrets, new PlaintextSecretProtector());
        var projectId = Guid.NewGuid();
        await secrets.AddAsync(projectId, "POSTMARK_SERVER_TOKEN", "token", DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        await connection.SaveSettingsAsync(
            projectId, "POSTMARK_SERVER_TOKEN",
            "hello@example.test", "Example", "outbound");
        await secrets.DeleteAsync(projectId, "POSTMARK_SERVER_TOKEN");
        await db.SaveChangesAsync();
        var options = Options.Create(new ClientCommsOptions());
        var factory = new StubHttpClientFactory(new StubHandler(
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))));
        var sender = new SendGridTwilioCommunicationSender(factory, options, connection);

        var capabilities = await sender.GetCapabilitiesAsync();

        Assert.False(capabilities.EmailEnabled);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed record CapturedRequest(
        IReadOnlyDictionary<string, string> Headers,
        string Body);

    private sealed class PlaintextSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string ciphertext) => ciphertext;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _send;
        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) => _send = send;
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => _send(request);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
