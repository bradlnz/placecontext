using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>
/// Builds a project's <see cref="DecisionTree"/> on demand from everything PlaceContext has logged:
/// its decisions, change ledger, and recent MCP tool activity. This is the seam that replaced the
/// graphify reader — handlers depend on it instead of an external graph file.
/// </summary>
public interface IDecisionTreeProvider
{
    Task<DecisionTree> BuildAsync(ProjectId projectId, CancellationToken ct = default);

    /// <summary>
    /// Drops any cached tree for the project so the next <see cref="BuildAsync"/> reassembles from
    /// scratch. No-op by default — only caching decorators carry state to drop. Callers that must
    /// observe brand-new data right now (the explicit rebuild action) invalidate first.
    /// </summary>
    void Invalidate(ProjectId projectId) { }
}
