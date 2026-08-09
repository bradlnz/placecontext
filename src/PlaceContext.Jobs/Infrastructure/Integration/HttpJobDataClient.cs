using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Jobs.Integration;

namespace PlaceContext.Jobs.Infrastructure.Integration;

public sealed class HttpJobDataClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<HttpJobDataClient> logger) : IJobDataClient
{
    public Task ProcessJobResultAsync(
        Job job,
        JobRun run,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            "job",
            job.Id,
            run.Id,
            run.ProjectId,
            PrimaryArtifact(run),
            RunDocuments(run),
            cancellationToken);

    public Task ProcessChainResultAsync(
        Guid chainId,
        Guid chainRunId,
        Guid projectId,
        string? primaryOutput,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            "chain",
            chainId,
            chainRunId,
            projectId,
            primaryOutput,
            [],
            cancellationToken);

    private async Task SendAsync(
        string sourceKind,
        Guid sourceId,
        Guid runId,
        Guid projectId,
        string? primaryOutput,
        IReadOnlyList<JobResultDocument> documents,
        CancellationToken cancellationToken)
    {
        var origin = configuration["PlaceContext:Jobs:Data:BaseAddress"]
            ?? configuration["PlaceContext:Microservices:Destinations:Data"];
        var apiKey = configuration["PlaceContext:Api:Key"];
        if (string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning(
                "Skipping Data enrichment for run {RunId}: Data destination or service API key is not configured.",
                runId);
            return;
        }

        var baseAddress = origin.EndsWith("/", StringComparison.Ordinal) ? origin : origin + "/";
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(baseAddress), "api/data/internal/job-results"))
        {
            Content = JsonContent.Create(new ProcessJobResultRequest(
                sourceKind,
                sourceId,
                runId,
                projectId,
                primaryOutput,
                documents)),
        };
        request.Headers.Add("X-Api-Key", apiKey);

        try
        {
            using var response = await httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Data enrichment request failed for run {RunId}.", runId);
        }
    }

    private static string? PrimaryArtifact(JobRun run)
    {
        if (run.ReduceResult?.Artifact is { Length: > 0 } reduced)
            return reduced;

        var shards = run.ShardResults
            .OrderBy(result => result.Index)
            .Where(result => !string.IsNullOrWhiteSpace(result.Artifact))
            .Select(result => result.Artifact!)
            .ToList();
        return shards.Count switch
        {
            0 => null,
            1 => shards[0],
            _ => "[" + string.Join(",", shards) + "]",
        };
    }

    private static IReadOnlyList<JobResultDocument> RunDocuments(JobRun run) =>
        run.ShardResults.SelectMany(result => result.Artifacts)
            .Concat(run.ReduceResult?.Artifacts ?? Array.Empty<RunArtifact>())
            .Select(artifact => new JobResultDocument(artifact.Name, artifact.Content, artifact.IsBinary))
            .ToList();

    private sealed record ProcessJobResultRequest(
        string SourceKind,
        Guid SourceId,
        Guid RunId,
        Guid ProjectId,
        string? PrimaryOutput,
        IReadOnlyList<JobResultDocument> Documents);

    private sealed record JobResultDocument(string Name, string Content, bool IsBinary);
}
