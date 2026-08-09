using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using PlaceContext.Jobs.Integration;

namespace PlaceContext.Jobs.Infrastructure.Integration;

public sealed class HttpJobLaunchpadClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : IJobLaunchpadClient
{
    public async Task<Guid> RunLaunchpadAsync(
        Guid projectId,
        string triggerName,
        string prompt,
        string? sourceTable,
        Guid chainId,
        CancellationToken ct = default)
    {
        var origin = configuration["PlaceContext:Jobs:AgentChat:BaseAddress"]
            ?? configuration["PlaceContext:Microservices:Destinations:AgentChat"]
            ?? throw new InvalidOperationException("Configure the AgentChat service destination for Jobs launchpads.");
        var apiKey = configuration["PlaceContext:Api:Key"]
            ?? throw new InvalidOperationException("Configure PlaceContext:Api:Key for service calls.");
        var baseAddress = origin.EndsWith("/", StringComparison.Ordinal) ? origin : origin + "/";
        using var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(baseAddress), "api/agent-chat/internal/launchpads"))
        {
            Content = JsonContent.Create(new { projectId, triggerName, prompt, sourceTable, chainId }),
        };
        request.Headers.Add("X-Api-Key", apiKey);
        using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<Response>(ct)
            ?? throw new InvalidOperationException("Agents returned no launchpad identifier.");
        return result.Id;
    }
}
