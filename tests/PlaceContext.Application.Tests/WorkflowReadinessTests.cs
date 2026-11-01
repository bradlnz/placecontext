using PlaceContext.Application.Dtos;
using PlaceContext.Application.Workflows;

namespace PlaceContext.Application.Tests;

public sealed class WorkflowReadinessTests
{
    [Fact]
    public void Missing_or_foreign_jobs_block_definition_readiness()
    {
        var missing = new JobChainStageView([new JobChainStepView(Guid.NewGuid(), "deleted")]);
        var result = WorkflowReadiness.Evaluate(Guid.NewGuid(), [missing], []);
        Assert.False(result.Ready);
        Assert.Contains(result.Findings, x => x.Severity == "error"
            && x.Title == "Unresolved job");
    }

    [Fact]
    public void No_steps_is_not_an_executable_workflow()
    {
        var result = WorkflowReadiness.Evaluate(Guid.NewGuid(), [], []);
        Assert.False(result.Ready);
        Assert.Contains(result.Findings, x => x.Title == "No workflow steps");
    }

    [Fact]
    public void External_actions_without_prior_human_step_are_warnings_not_fabricated_approvals()
    {
        var send = new JobChainStageView([], Action: new SendSmsChainActionView("+61400000000", "Welcome"));
        var result = WorkflowReadiness.Evaluate(Guid.NewGuid(), [send], []);
        Assert.True(result.Ready);
        Assert.Equal(1, result.WarningCount);
        Assert.Contains(result.Findings, x => x.Title == "External action without earlier human step");

        var approval = new JobChainStageView([], Action:
            new LoopChainActionView("humanInput", "Approve sending the message?", "approval", "yesNo"));
        var gated = WorkflowReadiness.Evaluate(Guid.NewGuid(), [approval, send], []);
        Assert.True(gated.Ready);
        Assert.Equal(0, gated.WarningCount);
    }

    [Fact]
    public void Human_review_on_an_else_branch_does_not_satisfy_the_main_path()
    {
        var approval = new JobChainStageView([], Action:
            new LoopChainActionView("humanInput", "Review the data", "approved", "yesNo"));
        var conditional = new JobChainStageView([], ElseBranch: [approval],
            Action: new LoopChainActionView("agentInput", "Verify evidence", "assessment"));
        var downstream = new JobChainStageView([], Action:
            new SendSmsChainActionView("+61400000000", "Process completed"));
        var result = WorkflowReadiness.Evaluate(Guid.NewGuid(), [conditional, downstream], []);
        Assert.Equal(1, result.WarningCount);
        Assert.Contains(result.Findings, f => f.Title == "External action without earlier human step");
    }

    [Fact]
    public void Empty_stages_are_reported_without_executing_anything()
    {
        var empty = new JobChainStageView([]);
        var result = WorkflowReadiness.Evaluate(Guid.NewGuid(), [empty], []);
        Assert.False(result.Ready);
        Assert.Contains(result.Findings, x => x.Title == "Empty workflow step");
    }
}
