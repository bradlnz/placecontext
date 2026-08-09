using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using PlaceContext.Domain.Entities;
using PlaceContext.Artifacts.Integration;

namespace PlaceContext.Artifacts.Infrastructure.Integration;

public sealed class HttpArtifactDataClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : IArtifactDataClient
{
    public async Task StoreOcrResultAsync(
        RunArtifactLink artifact,
        string markdown,
        DateTimeOffset ingestedAt,
        CancellationToken cancellationToken = default)
    {
        var origin = configuration["PlaceContext:Artifacts:Data:BaseAddress"]
            ?? configuration["PlaceContext:Microservices:Destinations:Data"]
            ?? throw new InvalidOperationException(
                "Configure the Data service destination for OCR result storage.");
        var apiKey = configuration["PlaceContext:Api:Key"]
            ?? throw new InvalidOperationException(
                "Configure PlaceContext:Api:Key for Artifacts-to-Data authentication.");
        var baseAddress = origin.EndsWith("/", StringComparison.Ordinal) ? origin : origin + "/";
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(baseAddress), "api/data/internal/ocr-results"))
        {
            Content = JsonContent.Create(new StoreOcrResultRequest(
                artifact.ProjectId,
                artifact.Id,
                artifact.RunId,
                artifact.JobId,
                artifact.Title,
                artifact.ContentType,
                markdown,
                ingestedAt)),
        };
        request.Headers.Add("X-Api-Key", apiKey);
        using var response = await httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private sealed record StoreOcrResultRequest(
        Guid ProjectId,
        Guid ArtifactId,
        Guid RunId,
        Guid JobId,
        string? Title,
        string? ContentType,
        string Markdown,
        DateTimeOffset IngestedAt);
}
