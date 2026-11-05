using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using Xunit;

namespace PlaceContext.Domain.Tests;

public class RepeatFlowTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Attempts_are_bounded(int attempts) => Assert.Throws<ArgumentException>(() => new RepeatFlowChainAction("true", attempts));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Completion_condition_is_required(string condition) => Assert.Throws<ArgumentException>(() => new RepeatFlowChainAction(condition));

    [Fact]
    public void Repeat_must_follow_work_and_be_last_without_a_gate()
    {
        var work = ChainStage.Of(Guid.NewGuid());
        var repeat = ChainStage.ForAction(new RepeatFlowChainAction("eq:ready:true"));
        Assert.Throws<ArgumentException>(() => Make([repeat]));
        Assert.Throws<ArgumentException>(() => Make([work, repeat, work]));
        Assert.Throws<ArgumentException>(() => Make([work, new ChainStage([], new ConditionGate("false"), action: repeat.Action)]));
        Assert.Throws<ArgumentException>(() => new ChainStage([Guid.NewGuid()], new ConditionGate("false", [repeat])));
        Assert.Equal(2, Make([work, repeat]).Stages.Count);
    }

    [Fact]
    public void Repeat_clears_old_handoff_and_keeps_attempt_count_in_durable_steps()
    {
        var chain = Make([ChainStage.ForAction(new LoopChainAction(LoopChainAction.TaskType, "Build", "result")),
            ChainStage.ForAction(new RepeatFlowChainAction("eq:ready:true"))]);
        var run = ChainRun.Start(chain, ["Build", "Repeat"], DateTimeOffset.UtcNow);
        run.AwaitInput(0, "{}", "old submitted response", null);
        run.RepeatFlow(1, "{\"ready\":false}", DateTimeOffset.UtcNow);
        Assert.Null(run.InputRequestJson);
        Assert.Equal(1, run.Steps[1].RetryCount);
        var restored = ChainRun.Rehydrate(run.Id, run.ChainId, run.ProjectId, run.ChainName, run.Status,
            run.Steps, run.FinalOutput, run.StartedAt, run.FinishedAt, run.ResumeAt, run.ResumeStageIndex);
        restored.Resume();
        Assert.Equal(1, restored.Steps[1].RetryCount);
        Assert.Equal("{\"ready\":false}", restored.FinalOutput);
        Assert.All(restored.Steps, step => Assert.Equal(ChainStepStatus.Pending, step.Status));
    }

    private static JobChain Make(IReadOnlyList<ChainStage> stages) => JobChain.Create(Guid.NewGuid(), "test", null, stages, DateTimeOffset.UtcNow);
}
