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
}
