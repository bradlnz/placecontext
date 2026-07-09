using PlaceContext.Domain.Entities;

namespace PlaceContext.Domain.Repositories;

/// <summary>Stored analytics charts, one per project+table (an upsert replaces the previous chart).</summary>
public interface IProjectChartRepository
{
    /// <summary>Insert the chart, replacing any existing chart for the same project+table.</summary>
    Task UpsertAsync(ProjectChart chart, CancellationToken ct = default);

    /// <summary>All of a project's charts, table-name-sorted.</summary>
    Task<IReadOnlyList<ProjectChart>> ListForProjectAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>Remove charts whose table no longer exists (table drops leave charts orphaned).</summary>
    Task DeleteForProjectAsync(Guid projectId, IReadOnlyCollection<string> keepTables, CancellationToken ct = default);
}
