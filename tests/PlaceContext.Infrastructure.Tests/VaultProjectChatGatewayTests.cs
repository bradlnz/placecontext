using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Infrastructure.Chat;
using Xunit;

namespace PlaceContext.Infrastructure.Tests;

public sealed class VaultProjectChatGatewayTests
{
    [Fact]
    public async Task Uses_external_llm_when_project_vault_has_api_token()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        var factory = new StubHttpClientFactory(new StubHandler(async request =>
        {
            captured = request;
            capturedBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"external reply\"}}]}", Encoding.UTF8, "application/json"),
            };
        }));
        var local = new FakeLocalGateway { Reply = "local reply" };
        var gateway = new VaultProjectChatGateway(
            factory, local, new FakeSecrets("LLM_API_TOKEN", "encrypted-token"),
            new FakeProtector("secret-token"), Configuration());

        var reply = await gateway.ChatAsync(
            Guid.NewGuid(),
            [new ChatMessage("user", "hello")],
            new ChatSettings(Model: "team-model")
        );

        Assert.Equal("external reply", reply);
        Assert.Equal(0, local.CallCount);
        Assert.Equal("Bearer", captured!.Headers.Authorization!.Scheme);
        Assert.Equal("secret-token", captured.Headers.Authorization.Parameter);
        Assert.Contains("\"model\":\"test-model\"", capturedBody);
    }

    [Fact]
    public async Task Uses_local_cluster_when_project_vault_has_no_api_token()
    {
        var local = new FakeLocalGateway { Reply = "local reply" };
        var gateway = new VaultProjectChatGateway(
            new StubHttpClientFactory(new StubHandler(_ => throw new InvalidOperationException())),
            local, new FakeSecrets(), new FakeProtector(""), Configuration());

        var reply = await gateway.ChatAsync(Guid.NewGuid(), [new ChatMessage("user", "hello")]);

        Assert.Equal("local reply", reply);
        Assert.Equal(1, local.CallCount);
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PlaceContext:ExternalLlm:Endpoint"] = "https://llm.example/v1/chat/completions",
            ["PlaceContext:ExternalLlm:Model"] = "test-model",
        }).Build();

    private sealed class FakeLocalGateway : IChatGateway
    {
        public bool IsEnabled => true;
        public int CallCount { get; private set; }
        public string Reply { get; init; } = "";
        public Task<string> ChatAsync(IReadOnlyList<ChatMessage> messages, ChatSettings? settings = null, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(Reply);
        }
    }

    private sealed class FakeSecrets(params string[] values) : IProjectSecretRepository
    {
        public Task<IReadOnlyDictionary<string, string>> GetCiphersAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(values.Chunk(2).ToDictionary(value => value[0], value => value[1]));
        public Task<IReadOnlyList<(string Name, DateTimeOffset CreatedAt)>> ListAsync(Guid projectId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(Guid projectId, string name, string cipher, DateTimeOffset now, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid projectId, string name, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeProtector(string value) : ISecretProtector
    {
        public string Protect(string plaintext) => throw new NotSupportedException();
        public string Unprotect(string ciphertext) => value;
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request);
    }
}
