using System.Collections.Concurrent;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.TestSupport;

public sealed class FakeDecisionTreeProvider : IDecisionTreeProvider
{
    public DecisionTree Tree { get; set; } = DecisionTree.Empty;
    public Task<DecisionTree> BuildAsync(ProjectId projectId, CancellationToken ct = default)
        => Task.FromResult(Tree);
}
