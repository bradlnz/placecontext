using System.Text.Json;
using PlaceContext.Application.Build;
using PlaceContext.Host.Workflows;

namespace PlaceContext.Host.Tests;

public sealed class AgentMarkupTests
{
    [Fact]
    public void Yaml_preserves_instructions_identity_links_and_permissions()
    {
        var agent = new DashboardAgentProfile
        {
            Name = "true", Model = "00123", Instructions = "Review: \"evidence\"\nKeep the result private.",
            ParentAgentId = Guid.NewGuid(), ConnectionId = Guid.NewGuid(),
            WorkflowIds = [Guid.NewGuid()], McpConnectionIds = [Guid.NewGuid()],
            WorkflowAccess = "read", TableAccess = "none", SourceCapabilities = [], ArtifactAccess = "none"
        };
        var yaml = AgentMarkup.Write(agent);
        Assert.Contains($"parentAgentId: {agent.ParentAgentId}", yaml);
        Assert.DoesNotContain("effective", yaml);
        Assert.Equal(JsonSerializer.Serialize(agent), JsonSerializer.Serialize(AgentMarkup.Read(yaml, agent.Id)));
        Assert.Throws<ArgumentException>(() => AgentMarkup.Read(yaml, Guid.NewGuid()));
    }

    [Fact]
    public void Yaml_shows_the_default_parent_and_keeps_the_parent_selector_in_sync()
    {
        var agent = new DashboardAgentProfile();
        var yaml = AgentMarkup.Write(agent);
        Assert.Contains($"parentAgentId: {DashboardSettings.DefaultAgentId}", yaml);
        var restored = AgentMarkup.Read(yaml, agent.Id);
        Assert.Null(restored.ParentAgentId);
        Assert.Equal(DashboardSettings.DefaultAgentId, restored.EffectiveParentAgentId);
        Assert.Null(agent.ParentAgentId);

        var parentId = Guid.NewGuid();
        var edited = yaml.Replace($"parentAgentId: {DashboardSettings.DefaultAgentId}", $"parentAgentId: {parentId}");
        Assert.Equal(parentId, AgentMarkup.Read(edited, agent.Id).ParentAgentId);

        var master = new DashboardAgentProfile { Id = DashboardSettings.DefaultAgentId };
        var masterYaml = AgentMarkup.Write(master);
        Assert.Contains("parentAgentId:", masterYaml);
        Assert.Null(AgentMarkup.Read(masterYaml, master.Id).EffectiveParentAgentId);
    }

    [Theory]
    [InlineData("name: second")]
    [InlineData("unknown: value")]
    [InlineData("name: &x [*x]")]
    [InlineData("name: !!str hello")]
    [InlineData("---\nname: next")]
    [InlineData("name: [unclosed")]
    public void Invalid_configuration_is_rejected(string extra)
    {
        var agent = new DashboardAgentProfile();
        Assert.ThrowsAny<Exception>(() => AgentMarkup.Read(AgentMarkup.Write(agent) + "\n" + extra, agent.Id));
    }

    [Fact]
    public void Invalid_permissions_and_oversized_input_are_rejected()
    {
        var agent = new DashboardAgentProfile { WorkflowAccess = "owner" };
        Assert.Throws<ArgumentException>(() => AgentMarkup.Read(AgentMarkup.Write(agent), agent.Id));
        Assert.Throws<ArgumentException>(() => AgentMarkup.Read(new string('a', 65537), agent.Id));
    }
}
