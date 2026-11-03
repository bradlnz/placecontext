using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using PlaceContext.Application.Search;
using PlaceContext.Application.Secrets;
using PlaceContext.Application.Tenancy;
using PlaceContext.Domain.Entities;
using PlaceContext.Infrastructure.Security;
using PlaceContext.Infrastructure.VectorStore;

namespace PlaceContext.Infrastructure.Tests;

public sealed class VectorPayloadEncryptionTests
{
    [Fact]
    public async Task Content_text_and_source_keys_are_encrypted_before_storage_and_round_trip()
    {
        using var api = new VectorApi();
        var encryptor = new DataProtectionEncryptor(new EphemeralDataProtectionProvider());
        var tenant = new Tenant();
        var indexer = new VectorContentIndexer(new Embeddings(), tenant, api, "http://vector", encryptor);
        var projectId = Guid.NewGuid();
        await indexer.IndexAsync(projectId, "customer", "person@example.com", "Name: Example Person");
        var point = Assert.Single(api.Points);
        var payload = point.GetProperty("payload");
        Assert.True(encryptor.IsProtected(payload.GetProperty("text").GetString()));
        Assert.True(encryptor.IsProtected(payload.GetProperty("source_key").GetString()));
        Assert.DoesNotContain("person@example.com", point.GetRawText());
        Assert.DoesNotContain("Example Person", point.GetRawText());
        var hit = Assert.Single(await indexer.SearchAsync(projectId, "customer"));
        Assert.Equal("person@example.com", hit.SourceKey);
        Assert.Equal("Name: Example Person", hit.Text);
        Assert.Contains(tenant.TenantId.ToString("N"), api.SearchBody);
        Assert.Contains(projectId.ToString("N"), api.SearchBody);
    }

    [Fact]
    public async Task Run_text_is_encrypted_and_the_uploaded_points_are_a_flat_array()
    {
        using var api = new VectorApi();
        var encryptor = new DataProtectionEncryptor(new EphemeralDataProtectionProvider());
        var repository = new VectorRunEmbeddingRepository(new Embeddings(), new Tenant(), api, "http://vector", encryptor);
        var embedding = RunEmbedding.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Email: person@example.com", [1, 0], DateTimeOffset.UtcNow);
        await repository.AddAsync(embedding);
        var point = Assert.Single(api.Points);
        var text = point.GetProperty("payload").GetProperty("text").GetString();
        Assert.True(encryptor.IsProtected(text));
        Assert.Equal(embedding.Text, encryptor.Unprotect(text, IDataEncryptor.Purpose.EmbeddingText));
        Assert.DoesNotContain("person@example.com", point.GetRawText());
        var hit = Assert.Single(await repository.SearchAsync(embedding.ProjectId, [1, 0], 5));
        Assert.Equal(embedding.Text, hit.Embedding.Text);
    }

    private sealed class Tenant : ICurrentTenant
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public string Slug => "test";
        public string TimeZoneId => "UTC";
        public bool IsResolved => true;
    }

    private sealed class Embeddings : IEmbeddingGateway
    {
        public bool IsEnabled => true;
        public int Dimensions => 2;
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new float[] { 1, 0 }).ToArray());
    }

    private sealed class VectorApi : HttpMessageHandler, IHttpClientFactory
    {
        public List<JsonElement> Points { get; } = [];
        public string SearchBody = "";
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = await request.Content!.ReadAsStringAsync(ct);
            if (request.Method == HttpMethod.Put && path.EndsWith("/points"))
            {
                using var json = JsonDocument.Parse(body);
                Points.AddRange(json.RootElement.GetProperty("points").EnumerateArray().Select(point => point.Clone()));
            }
            if (path.EndsWith("/search"))
            {
                SearchBody = body;
                return Reply(JsonSerializer.Serialize(new { result = Points }));
            }
            return Reply("{\"result\":true}");
        }
        private static HttpResponseMessage Reply(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
