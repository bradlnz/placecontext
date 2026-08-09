namespace PlaceContext.Data.Integration;

public interface IDataJobsClient
{
    Task<DataJobCatalog> GetCatalogAsync(Guid projectId, CancellationToken ct = default);
}

public sealed record DataJobCatalog(
    IReadOnlyList<DataJobSummary> Jobs,
    IReadOnlyList<DataChainSummary> Chains,
    IReadOnlyList<DataRunSummary> Runs);

public sealed record DataJobSummary(
    Guid Id,
    Guid ProjectId,
    string Name,
    string? Description,
    string ReturnType);

public sealed record DataChainSummary(
    Guid Id,
    Guid ProjectId,
    string Name,
    string? Description,
    IReadOnlyList<DataChainStageSummary> Stages);

public sealed record DataChainStageSummary(IReadOnlyList<Guid> JobIds);

public sealed record DataRunSummary(
    Guid Id,
    Guid JobId,
    string Status,
    DateTimeOffset StartedAt);

public sealed record DataArtifactSummary(
    Guid Id,
    Guid RunId,
    Guid JobId,
    string Title,
    string Kind,
    string ContentType);
