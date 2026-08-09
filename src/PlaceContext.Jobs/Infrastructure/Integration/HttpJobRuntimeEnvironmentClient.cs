using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PlaceContext.Jobs.Integration;

namespace PlaceContext.Jobs.Infrastructure.Integration;

public sealed class HttpJobRuntimeEnvironmentClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<HttpJobRuntimeEnvironmentClient> logger) : IJobRuntimeEnvironmentClient
{
    public async Task<IReadOnlyDictionary<string, string>> GetEnvironmentAsync(
        Guid projectId,
        IReadOnlyList<Guid> mcpConnectionIds,
        CancellationToken ct = default)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        await MergeAsync(values, "Vault", "api/vault/internal", $"projects/{projectId}/secrets/resolve-all", null, ct);
        await MergeAsync(values, "Search", "api/search/internal", $"projects/{projectId}/job-environment", null, ct);
        if (mcpConnectionIds.Count > 0)
            await MergeAsync(values, "Mcp", "api/mcp/internal", "job-environment", new { connectionIds = mcpConnectionIds }, ct);
        return values;
    }

    private async Task MergeAsync(
        Dictionary<string, string> into,
        string service,
        string routeRoot,
        string path,
        object? body,
        CancellationToken ct)
    {
        var origin = configuration[$"PlaceContext:Jobs:{service}:BaseAddress"]
            ?? configuration[$"PlaceContext:Microservices:Destinations:{service}"];
        var apiKey = configuration["PlaceContext:Api:Key"];
        if (string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(apiKey)) return;
        var baseAddress = origin.EndsWith("/", StringComparison.Ordinal) ? origin : origin + "/";
        using var request = new HttpRequestMessage(
            body is null ? HttpMethod.Get : HttpMethod.Post,
            new Uri(new Uri(baseAddress), $"{routeRoot}/{path}"));
        request.Headers.Add("X-Api-Key", apiKey);
        if (body is not null) request.Content = JsonContent.Create(body);
        try
        {
            using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var received = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(ct) ?? [];
            foreach (var pair in received) into[pair.Key] = pair.Value;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Unable to load runtime environment from {Service}.", service);
        }
    }
}
