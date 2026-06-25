using System.Collections.Concurrent;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.TestSupport;

public sealed class InMemoryRiskAssessmentRepository : IRiskAssessmentRepository
{
    private readonly List<RiskAssessment> _store = new();

    public Task AddAsync(RiskAssessment assessment, CancellationToken ct = default)
    {
        _store.Add(assessment);
        return Task.CompletedTask;
    }

    public Task<RiskAssessment?> GetLatestAsync(ProjectId projectId, CancellationToken ct = default)
        => Task.FromResult(_store.Where(a => a.ProjectId == projectId)
            .OrderByDescending(a => a.ComputedAt).FirstOrDefault());

    public Task<IReadOnlyList<RiskAssessment>> ListForProjectAsync(ProjectId projectId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RiskAssessment>>(
            _store.Where(a => a.ProjectId == projectId).ToList());
}
