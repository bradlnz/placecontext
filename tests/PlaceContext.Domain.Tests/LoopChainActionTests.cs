using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Tests;

public class LoopChainActionTests
{
    [Theory]
    [InlineData("unknown", "Review", "response")]
    [InlineData(LoopChainAction.HumanType, " ", "response")]
    [InlineData(LoopChainAction.AgentType, "Review", "")]
    [InlineData(LoopChainAction.HumanType, "Review", "a.b")]
    [InlineData(LoopChainAction.HumanType, "Review", "1response")]
    public void Rejects_invalid_configuration(string kind, string instructions, string response)
        => Assert.Throws<ArgumentException>(() => new LoopChainAction(kind, instructions, response));

    [Fact]
    public void Yes_no_is_a_human_choice_and_old_steps_default_to_text()
    {
        Assert.Equal(LoopChainAction.TextResponse, new LoopChainAction(LoopChainAction.HumanType, "Why?", "answer").ResponseType);
        Assert.Equal(LoopChainAction.YesNoResponse, new LoopChainAction(LoopChainAction.HumanType, "Continue?", "approved", LoopChainAction.YesNoResponse).ResponseType);
        Assert.Throws<ArgumentException>(() => new LoopChainAction(LoopChainAction.HumanType, "Continue?", "answer", "unknown"));
        Assert.Throws<ArgumentException>(() => new LoopChainAction(LoopChainAction.AgentType, "Continue?", "answer", LoopChainAction.YesNoResponse));
    }

    [Fact]
    public void Nested_input_is_rejected_until_branch_continuations_are_supported()
    {
        var input = ChainStage.ForAction(new LoopChainAction(LoopChainAction.HumanType, "Review", "review"));
        Assert.Throws<ArgumentException>(() => new ChainStage([Guid.NewGuid()],
            new ConditionGate("true", [input])));
        Assert.Throws<ArgumentException>(() => new ChainStage([Guid.NewGuid()], elseBranch: [input]));
        Assert.NotNull(new ChainStage([], new ConditionGate("eq:needs_review:true"), action: input.Action));
    }
}
