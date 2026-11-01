using PlaceContext.Application.Dtos;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class WorkflowRunMonitoringTests
{
    [Fact]
    public void Human_input_status_is_distinct_from_regular_waiting_and_model_review()
    {
        var request = new ChainInputRequest(Guid.NewGuid(),
            [new ChainInputStep(0, "Human review", [new JobParameterDto("approved")])],
            Kind: LoopChainAction.HumanType,
            Instructions: "Review before contacting the lead",
            ResponseType: LoopChainAction.YesNoResponse);
        var run = new ChainRunView(Guid.NewGuid(), Guid.NewGuid(), "Lead onboarding",
            "AwaitingInput", [], null, DateTimeOffset.UtcNow, null)
            { InputRequest = request, ProjectId = Guid.NewGuid() };
        Assert.True(JobChainsViewModel.NeedsHumanInput(run));
        Assert.False(JobChainsViewModel.NeedsHumanInput(run with { Status = "Waiting" }));
        Assert.False(JobChainsViewModel.NeedsHumanInput(run with
        {
            InputRequest = request with { Kind = LoopChainAction.AgentType }
        }));
        Assert.False(JobChainsViewModel.NeedsHumanInput(run with { InputRequest = null }));
    }
}
