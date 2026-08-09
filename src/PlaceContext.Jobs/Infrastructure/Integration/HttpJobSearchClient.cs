using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PlaceContext.Jobs.Integration;

namespace PlaceContext.Jobs.Infrastructure.Integration;

public sealed class HttpJobSearchClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<HttpJobSearchClient> logger) : IJobSearchClient
{
    public async Task IndexRunOutputAsync(
        Guid runId,
        Guid jobId,
        Guid projectId,
        string text,
        CancellationToken cancellationToken = default)
    {
        var origin = configuration["PlaceContext:Jobs:Search:BaseAddress"]
            ?? configuration["PlaceContext:Microservices:Destinations:Search"];
        var apiKey = configuration["PlaceContext:Api:Key"];
        if (string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning(
                "Skipping run-output indexing for run {RunId}: Search destination or service API key is not configured.",
                runId);
            return;
        }

        var baseAddress = origin.EndsWith("/", StringComparison.Ordinal) ? origin : origin + "/";
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(baseAddress), "api/search/internal/run-outputs"))
        {
            Content = JsonContent.Create(new IndexRunOutputRequest(runId, jobId, projectId, text)),
        };
        request.Headers.Add("X-Api-Key", apiKey);

        try
        {
            using var response = await httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Run-output indexing failed for run {RunId}.", runId);
        }
    }
}
