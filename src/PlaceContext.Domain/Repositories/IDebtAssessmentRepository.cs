using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Repositories;

/// <summary>Persistence of immutable debt-assessment snapshots (history → trend).</summary>
public interface IDebtAssessmentRepository
{
    Task AddAsync(DebtAssessment assessment, CancellationToken ct = default);
    Task<DebtAssessment?> GetLatestAsync(ProjectId projectId, CancellationToken ct = default);
    Task<IReadOnlyList<DebtAssessment>> ListForProjectAsync(ProjectId projectId, CancellationToken ct = default);
}
