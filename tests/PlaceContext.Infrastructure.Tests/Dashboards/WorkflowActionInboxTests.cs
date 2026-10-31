using PlaceContext.Application.Dtos;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Infrastructure.Dashboards;

namespace PlaceContext.Infrastructure.Tests.Dashboards;

public sealed class WorkflowActionInboxTests
{
    private static ChainRunView Paused(string status = "AwaitingInput",
        string kind = LoopChainAction.HumanType,
        string response = LoopChainAction.YesNoResponse, string fieldType = "checkbox")
    {
        var request = new ChainInputRequest(Guid.NewGuid(),
            [new ChainInputStep(3, "Human in the loop", [new JobParameterDto("approval", "Response", Type: fieldType)])],
            Kind: kind, Instructions: "Approve lead onboarding?", ResponseType: response);
        return new ChainRunView(Guid.NewGuid(), Guid.NewGuid(), "Lead onboarding", status, [],
            null, DateTimeOffset.UtcNow, null)
            { ProjectId = Guid.NewGuid(), InputRequest = request };
    }

    [Fact]
    public void Only_real_unsubmitted_human_yes_no_checkpoints_are_actionable()
    {
        var run = Paused();
        var action = Assert.IsType<PlaceContext.Application.Dashboards.WorkflowApprovalAction>(
            WorkflowActionInbox.TryCreate(run));
        Assert.Equal(run.Id, action.ChainRunId);
        Assert.Equal(run.InputRequest!.Id, action.RequestId);
        Assert.Equal(3, action.StepIndex);
        Assert.Equal("approval", action.ResponseField);
        Assert.Null(WorkflowActionInbox.TryCreate(Paused(status: "Waiting")));
        Assert.Null(WorkflowActionInbox.TryCreate(Paused(kind: LoopChainAction.AgentType)));
        Assert.Null(WorkflowActionInbox.TryCreate(Paused(response: LoopChainAction.TextResponse)));
        Assert.Null(WorkflowActionInbox.TryCreate(Paused(fieldType: "text")));
        Assert.Null(WorkflowActionInbox.TryCreate(run with
        {
            InputRequest = run.InputRequest with { SubmittedValues = "submitted" }
        }));
        Assert.Null(WorkflowActionInbox.TryCreate(run with { InputRequest = null }));
    }
}
