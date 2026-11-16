using PlaceContext.Application.Features;
using PlaceContext.Application.Build;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;

namespace PlaceContext.Application.Tests;

public sealed class WorkflowBranchMappingTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Automatic_if_rejects_empty_questions(string question)
    {
        Assert.Throws<ArgumentException>(() => new WorkflowStage([Guid.NewGuid()], ReviewQuestion: question).ToStage());
        Assert.Throws<ArgumentException>(() => new WorkflowStage([Guid.NewGuid()], ReviewQuestion: new string('x', 4001)).ToStage());
    }

    [Fact]
    public async Task Automatic_if_survives_assistant_and_service_mapping()
    {
        var workflow = new WorkflowStage([Guid.NewGuid()], ReviewQuestion: "Technical question?", ElseBranch: [new([Guid.NewGuid()])]);
        var chain = JobChain.Create(Guid.NewGuid(), "Automatic", null, [workflow.ToStage()], DateTimeOffset.UtcNow);
        var chains = new InMemoryJobChainRepository(); await chains.AddAsync(chain);
        var view = Assert.Single(await new ListJobChainsHandler(chains, new InMemoryJobRepository()).HandleAsync(new(chain.ProjectId)));
        var mapped = WorkflowStage.FromView(Assert.Single(view.Stages));
        Assert.Equal(workflow.ReviewQuestion, mapped.ReviewQuestion);
        var gate = Assert.IsType<ConditionGate>(mapped.ToStage().Gate);
        Assert.Equal(workflow.ReviewQuestion, gate.ReviewQuestion);
        Assert.Equal(workflow.ElseBranch![0].JobIds, Assert.Single(gate.ElseBranch!).JobIds);
        Assert.Throws<InvalidOperationException>(() => gate.Evaluate("{\"ai_gate_review\":{\"noul\":1}}"));
        Assert.Throws<ArgumentException>(() => (workflow with { WaitSeconds = 10 }).ToStage());
    }

    [Fact]
    public async Task Listing_workflows_preserves_nested_condition_branches_for_editing()
    {
        var projectId = Guid.NewGuid();
        var first = Guid.NewGuid();
        var fallback = Guid.NewGuid();
        var nested = Guid.NewGuid();
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
