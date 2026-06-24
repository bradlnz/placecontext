using System.Collections.Concurrent;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.TestSupport;

public sealed class InMemoryDebtAssessmentRepository : IDebtAssessmentRepository
{
    private readonly List<DebtAssessment> _store = new();

    public Task AddAsync(DebtAssessment assessment, CancellationToken ct = default)
    {
        _store.Add(assessment);
        return Task.CompletedTask;
    }

    public Task<DebtAssessment?> GetLatestAsync(ProjectId projectId, CancellationToken ct = default)
        => Task.FromResult(_store.Where(a => a.ProjectId == projectId)
            .OrderByDescending(a => a.ComputedAt).FirstOrDefault());

    public Task<IReadOnlyList<DebtAssessment>> ListForProjectAsync(ProjectId projectId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DebtAssessment>>(
            _store.Where(a => a.ProjectId == projectId).ToList());
}
