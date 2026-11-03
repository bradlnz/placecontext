namespace PlaceContext.Application.ProjectData;

public interface IProjectDataReader
{
    /// <summary>Run one read-only query within the project's isolated database, with bounded rows and execution time.</summary>
    Task<ProjectQueryResult> QueryAsync(Guid projectId, string sql, CancellationToken ct = default);
}
