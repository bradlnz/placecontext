using PlaceContext.Domain.Entities;
using Xunit;

namespace PlaceContext.Domain.Tests;

public sealed class AgentDefinitionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Command_agent_is_created_with_graph_knowledge_and_full_control()
    {
        var projectId = Guid.NewGuid();

        var agent = AgentDefinition.CreateCommand(projectId, Now);

        Assert.Equal(AgentKind.Command, agent.Kind);
        Assert.Equal("Command Agent", agent.Name);
        Assert.True(agent.Enabled);
        Assert.Contains(AgentCapability.GraphRead, agent.Capabilities);
        Assert.Contains(AgentCapability.JobsRun, agent.Capabilities);
        Assert.Empty(agent.AllowedJobIds);

        agent.Update("Command Agent", "", "Coordinate", "command", [], [], false, null, Now.AddMinutes(1), "{}");
        Assert.True(agent.Enabled);
    }

    [Fact]
    public void Worker_agent_requires_a_name_and_always_has_graph_access()
    {
        Assert.Throws<ArgumentException>(() => AgentDefinition.CreateWorker(
            Guid.NewGuid(), " ", "", "", "custom", "{}", [], [], null, Now));

        var agent = AgentDefinition.CreateWorker(
            Guid.NewGuid(), "Researcher", "Finds evidence", "Use primary evidence.", "research",
            "{}", [AgentCapability.ArtifactsRead], [], null, Now);

        Assert.Contains(AgentCapability.GraphRead, agent.Capabilities);
        Assert.Contains(AgentCapability.ArtifactsRead, agent.Capabilities);
    }

    [Fact]
    public void Update_normalizes_capabilities_and_job_allowlist()
    {
        var firstJob = Guid.NewGuid();
        var agent = AgentDefinition.CreateWorker(
            Guid.NewGuid(), "Operator", "", "", "job-operator",
            "{}", [AgentCapability.JobsRun], [firstJob, firstJob, Guid.Empty], null, Now);

        agent.Update(
            " Operator ", " Runs approved jobs ", " Be careful. ", "job-operator",
            [AgentCapability.JobsRun, AgentCapability.JobsRun],
            [firstJob, firstJob, Guid.Empty], false, null, Now.AddMinutes(1), "{}");

        Assert.Equal("Operator", agent.Name);
        Assert.Equal("Runs approved jobs", agent.Description);
        Assert.Equal("Be careful.", agent.Instructions);
        Assert.False(agent.Enabled);
        Assert.Equal([AgentCapability.GraphRead, AgentCapability.JobsRun], agent.Capabilities.Order());
        Assert.Equal([firstJob], agent.AllowedJobIds);
    }
}
