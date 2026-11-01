using PlaceContext.Application.Dtos;
using PlaceContext.Application.Workflows;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Infrastructure.Workflows;

namespace PlaceContext.Infrastructure.Tests.Workflows;

public sealed class WorkflowMonitorServiceTests
{
    private static ChainRunView Waiting(
        string status = "AwaitingInput", string? kind = LoopChainAction.HumanType,
        string responseType = LoopChainAction.YesNoResponse, string fieldType = "checkbox")
    {
        var input = new ChainInputRequest(Guid.NewGuid(),
            [new ChainInputStep(2, "Human review", [new JobParameterDto("approved", "Approve", Type: fieldType)])],
            Kind: kind, Instructions: "Approve this onboarding lead?", ResponseType: responseType);
        return new ChainRunView(Guid.NewGuid(), Guid.NewGuid(), "Lead onboarding", status,
            [], null, DateTimeOffset.UtcNow, null)
        {
            ProjectId = Guid.NewGuid(), InputRequest = input
        };
    }

    [Fact]
    public void Only_a_live_human_yes_no_checkpoint_is_directly_approvable()
    {
        var run = Waiting();
        var action = Assert.IsType<WorkflowHumanAction>(WorkflowMonitorService.PendingHumanAction(run));
        Assert.True(action.CanAnswerYesNo);
        Assert.True(action.IsApproval);
        Assert.Equal("approved", action.ResponseField);
        Assert.Equal(run.InputRequest!.Id, action.RequestId);
        Assert.Equal(run.Id, action.RunId);

        Assert.Null(WorkflowMonitorService.PendingHumanAction(Waiting(status: "Waiting")));
        Assert.Null(WorkflowMonitorService.PendingHumanAction(Waiting(kind: LoopChainAction.AgentType)));
        Assert.Null(WorkflowMonitorService.PendingHumanAction(run with
        {
            InputRequest = run.InputRequest with { SubmittedValues = "already submitted" }
        }));
        Assert.Null(WorkflowMonitorService.PendingHumanAction(run with { InputRequest = null }));
    }

    [Fact]
    public void Written_responses_and_missing_job_inputs_are_visible_but_not_approval_buttons()
    {
        var written = Assert.IsType<WorkflowHumanAction>(WorkflowMonitorService.PendingHumanAction(
            Waiting(responseType: LoopChainAction.TextResponse, fieldType: "text")));
        Assert.False(written.CanAnswerYesNo);
        Assert.Null(written.ResponseField);

        var jobInput = Assert.IsType<WorkflowHumanAction>(WorkflowMonitorService.PendingHumanAction(
            Waiting(kind: null, responseType: "text", fieldType: "number")));
        Assert.False(jobInput.CanAnswerYesNo);
        Assert.False(jobInput.IsApproval);
    }

    [Fact]
    public void Indicators_describe_only_the_bounded_completed_run_sample()
    {
        var time = DateTimeOffset.UtcNow;
        WorkflowRunIndicator[] sample =
        [
            new(Guid.NewGuid(), Guid.NewGuid(), "A", "Succeeded", 2, 2, time, time),
            new(Guid.NewGuid(), Guid.NewGuid(), "A", "Failed", 1, 2, time, time),
            new(Guid.NewGuid(), Guid.NewGuid(), "B", "Running", 0, 3, time, null),
            new(Guid.NewGuid(), Guid.NewGuid(), "B", "AwaitingInput", 1, 3, time, null)
        ];
        var result = new WorkflowMonitorSnapshot(time, 2, 2, 12, sample, [], true);
        Assert.Equal(2, result.CompletedCount);
        Assert.Equal(50, result.SuccessPercent);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(1, result.RunningCount);
        Assert.Null((result with { Runs = sample.Skip(2).ToArray() }).SuccessPercent);
    }
}
