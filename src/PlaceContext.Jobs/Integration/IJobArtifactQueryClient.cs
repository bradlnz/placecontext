namespace PlaceContext.Jobs.Integration;

public interface IJobArtifactQueryClient
{
    Task<bool> HasHtmlReportAsync(Guid runId, CancellationToken ct = default);
}
