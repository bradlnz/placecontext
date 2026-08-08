using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>
/// Builds a decision tree from its sources without applying a cache. Infrastructure decorators use
/// this shared seam without taking a dependency on the Search implementation assembly.
/// </summary>
public interface IUncachedDecisionTreeProvider
{
    Task<DecisionTree> BuildAsync(ProjectId projectId, CancellationToken ct = default);
}
