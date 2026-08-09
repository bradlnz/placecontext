using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using PlaceContext.Crm.Integration;

namespace PlaceContext.Crm.Infrastructure.Integration;

public sealed class HttpCrmArtifactsClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : ICrmArtifactsClient
{
    public async Task<IReadOnlyList<CrmRunArtifactSummary>> ListForRunAsync(
        Guid runId,
        CancellationToken ct = default)
    {
        var origin = configuration["PlaceContext:Crm:Artifacts:BaseAddress"]
            ?? configuration["PlaceContext:Microservices:Destinations:Artifacts"]
            ?? throw new InvalidOperationException("Configure the Artifacts service destination for CRM.");
        var apiKey = configuration["PlaceContext:Api:Key"]
            ?? throw new InvalidOperationException("Configure PlaceContext:Api:Key for service calls.");
        var baseAddress = origin.EndsWith("/", StringComparison.Ordinal) ? origin : origin + "/";
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(new Uri(baseAddress), $"api/artifacts/internal/runs/{runId}"));
        request.Headers.Add("X-Api-Key", apiKey);
        using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<CrmRunArtifactSummary>>(ct)
            ?? [];
    }

    public async Task<CrmStoredObject> StoreAsync(
        Guid projectId,
        Guid clientId,
        Guid objectId,
        byte[] content,
        string contentType,
        CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Post, "api/artifacts/internal/crm-objects");
        request.Content = JsonContent.Create(new
        {
            projectId,
            clientId,
            objectId,
            contentBase64 = Convert.ToBase64String(content),
            contentType,
        });
        using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CrmStoredObject>(ct)
            ?? throw new InvalidOperationException("The Artifacts service returned an empty storage response.");
    }

    public async Task<CrmArtifactContent?> ReadAsync(
        string bucket,
        string objectKey,
        CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Post, "api/artifacts/internal/crm-objects/read");
        request.Content = JsonContent.Create(new { bucket, objectKey });
        using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CrmArtifactContent>(ct);
    }

    public async Task DeleteAsync(
        string bucket,
        string objectKey,
        CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Delete, "api/artifacts/internal/crm-objects");
        request.Content = JsonContent.Create(new { bucket, objectKey });
        using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
    {
        var origin = configuration["PlaceContext:Crm:Artifacts:BaseAddress"]
            ?? configuration["PlaceContext:Microservices:Destinations:Artifacts"]
            ?? throw new InvalidOperationException("Configure the Artifacts service destination for CRM.");
        var apiKey = configuration["PlaceContext:Api:Key"]
            ?? throw new InvalidOperationException("Configure PlaceContext:Api:Key for service calls.");
        var baseAddress = origin.EndsWith("/", StringComparison.Ordinal) ? origin : origin + "/";
        var request = new HttpRequestMessage(method, new Uri(new Uri(baseAddress), relativePath));
        request.Headers.Add("X-Api-Key", apiKey);
        return request;
    }
}
