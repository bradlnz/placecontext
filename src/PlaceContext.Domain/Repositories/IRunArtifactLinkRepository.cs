using PlaceContext.Domain.Entities;

namespace PlaceContext.Domain.Repositories;

/// <summary>Persistence of <see cref="RunArtifactLink"/> records — post-job outputs stored per run.</summary>
public interface IRunArtifactLinkRepository
{
    Task AddAsync(RunArtifactLink link, CancellationToken ct = default);
    Task<IReadOnlyList<RunArtifactLink>> ListForRunAsync(Guid runId, CancellationToken ct = default);
    Task<RunArtifactLink?> GetByIdAsync(Guid id, CancellationToken ct = default);
}
