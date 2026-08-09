using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Jobs.Integration;

namespace PlaceContext.Jobs.Infrastructure.Integration;

public sealed class HttpJobArtifactClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<HttpJobArtifactClient> logger) : IJobArtifactClient
{
    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(configuration["PlaceContext:Jobs:Artifacts:BaseAddress"]
            ?? configuration["PlaceContext:Microservices:Destinations:Artifacts"])
        && !string.IsNullOrWhiteSpace(configuration["PlaceContext:Api:Key"]);

    public async Task StoreAsync(
        Guid projectId,
        Guid jobId,
        Guid runId,
        string jobName,
        PostJobActionKind kind,
        string fileName,
        string title,
        string contentType,
        byte[] content,
        CancellationToken ct = default)
    {
        var origin = configuration["PlaceContext:Jobs:Artifacts:BaseAddress"]
            ?? configuration["PlaceContext:Microservices:Destinations:Artifacts"];
        var apiKey = configuration["PlaceContext:Api:Key"];
        if (string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(apiKey)) return;
        var baseAddress = origin.EndsWith("/", StringComparison.Ordinal) ? origin : origin + "/";
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(baseAddress), "api/artifacts/internal/job-output"))
        {
            Content = JsonContent.Create(new StoreJobArtifactRequest(
                projectId, jobId, runId, jobName, kind.ToString(), fileName, title,
                contentType, Convert.ToBase64String(content))),
        };
        request.Headers.Add("X-Api-Key", apiKey);
        using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            logger.LogWarning("Artifacts service rejected output for run {RunId}: {Status}.", runId, response.StatusCode);
        response.EnsureSuccessStatusCode();
    }

    private sealed record StoreJobArtifactRequest(
        Guid ProjectId,
        Guid JobId,
        Guid RunId,
        string JobName,
        string Kind,
        string FileName,
        string Title,
        string ContentType,
        string ContentBase64);
}
