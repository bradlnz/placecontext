using PlaceContext.Application.Features;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;

namespace PlaceContext.Application.Tests;

public sealed class WorkflowBranchMappingTests
{
    [Fact]
    public async Task Listing_workflows_preserves_nested_condition_branches_for_editing()
    {
        var projectId = Guid.NewGuid();
        var first = Guid.NewGuid(); var fallback = Guid.NewGuid(); var nested = Guid.NewGuid();
        var chains = new InMemoryJobChainRepository();
        var chain = JobChain.Create(projectId, "conditional", null,
            [new ChainStage([first], new ConditionGate("exists:ok",
                [new ChainStage([fallback], new ConditionGate("exists:backup", [ChainStage.Of(nested)]))]))], DateTimeOffset.UtcNow);
        await chains.AddAsync(chain);
        var handler = new ListJobChainsHandler(chains, new InMemoryJobRepository());
        var view = Assert.Single(await handler.HandleAsync(new ListJobChainsQuery(projectId)));
        var branch = Assert.Single(Assert.Single(view.Stages).ElseBranch!);
        Assert.Equal(fallback, Assert.Single(branch.Jobs).JobId);
        Assert.Equal(nested, Assert.Single(Assert.Single(branch.ElseBranch!).Jobs).JobId);
    }
}
