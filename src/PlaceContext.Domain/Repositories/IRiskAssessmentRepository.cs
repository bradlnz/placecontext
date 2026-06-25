using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Repositories;

/// <summary>Persistence of immutable risk-assessment snapshots (history → trend).</summary>
public interface IRiskAssessmentRepository
{
    Task AddAsync(RiskAssessment assessment, CancellationToken ct = default);
    Task<RiskAssessment?> GetLatestAsync(ProjectId projectId, CancellationToken ct = default);
    Task<IReadOnlyList<RiskAssessment>> ListForProjectAsync(ProjectId projectId, CancellationToken ct = default);
}
