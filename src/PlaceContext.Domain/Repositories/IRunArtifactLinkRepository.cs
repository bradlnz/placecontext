using PlaceContext.Domain.Entities;

namespace PlaceContext.Domain.Repositories;

/// <summary>Persistence of <see cref="RunArtifactLink"/> records — post-job outputs stored per run.</summary>
public interface IRunArtifactLinkRepository
{
    Task AddAsync(RunArtifactLink link, CancellationToken ct = default);
    Task<IReadOnlyList<RunArtifactLink>> ListForRunAsync(Guid runId, CancellationToken ct = default);
    Task<RunArtifactLink?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Every run's post-job outputs for one job (newest run first) — the run-history panel's charts.</summary>
    Task<IReadOnlyList<RunArtifactLink>> ListForJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>The newest stored artifacts across every project (the Artifacts file viewer).</summary>
    Task<IReadOnlyList<RunArtifactLink>> ListRecentAsync(int take, CancellationToken ct = default);

    /// <summary>Every stored artifact for one project (newest first) — the project-scoped file viewer.</summary>
    Task<IReadOnlyList<RunArtifactLink>> ListForProjectAsync(Guid projectId, int take, CancellationToken ct = default);

    /// <summary>Permanently removes an artifact link row (the object bytes are deleted separately).</summary>
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}
