using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Tests;

public class LoopChainActionTests
{
    [Fact]
    public void Automatic_execution_requires_a_model_and_is_never_allowed_for_humans()
    {
        var connection = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => new LoopChainAction(LoopChainAction.TaskType, "Build", "result", agentProvider: LoopChainAction.AutomaticAgent));
        Assert.Throws<ArgumentException>(() => new LoopChainAction(LoopChainAction.TaskType, "Build", "result", agentProvider: LoopChainAction.AutomaticAgent, connectionId: connection, model: " "));
        Assert.Throws<ArgumentException>(() => new LoopChainAction(LoopChainAction.HumanType, "Review", "result", agentProvider: LoopChainAction.AutomaticAgent, connectionId: connection, model: "test"));
        Assert.Throws<ArgumentException>(() => new LoopChainAction(LoopChainAction.TaskType, "Build", "result", connectionId: connection, model: "test"));
        Assert.Equal("test", new LoopChainAction(LoopChainAction.TaskType, "Build", "result", agentProvider: LoopChainAction.AutomaticAgent, connectionId: connection, model: "test").Model);
    }

    [Fact]
    public void Jev_is_an_explicit_agent_choice_and_existing_steps_remain_external()
    {
        Assert.Equal(LoopChainAction.ExternalAgent, new LoopChainAction(LoopChainAction.AgentType, "Review", "answer").AgentProvider);
        var jev = new LoopChainAction(LoopChainAction.AgentType, "Review", "answer", agentProvider: LoopChainAction.JevAgent);
        Assert.Equal(LoopChainAction.JevAgent, jev.AgentProvider);
        Assert.Throws<ArgumentException>(() => new LoopChainAction(LoopChainAction.HumanType, "Review", "answer", agentProvider: LoopChainAction.JevAgent));
        Assert.Throws<ArgumentException>(() => new LoopChainAction(LoopChainAction.AgentType, "Review", "answer", agentProvider: "unknown"));
    }

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
