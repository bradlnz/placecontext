using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Build;
using PlaceContext.Application.Secrets;
using PlaceContext.Host.Components.ViewModels;
using PlaceContext.Infrastructure.Build;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Host.Tests;

public sealed partial class BuildChatTests
{
    [Fact]
    public async Task Agents_section_edits_the_private_dashboard_agent_without_publishing_or_replacing_tiles()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        var store = ProjectStore(f);
        var vm = new ProjectAgentsViewModel(store, f.Connections, new EmptyMcp(), f.Permissions, new(), f.Store);
        await vm.LoadAsync(f.ProjectId);
        var dashboard = Assert.Single(vm.Agents);
        Assert.Equal(Guid.Empty, dashboard.Id);
        vm.EditAgent(dashboard);
        Assert.True(vm.EditingPrivateAgent);
        Assert.False(vm.CanDelete);
        var settings = await f.Store.GetSettingsAsync(f.ProjectId);
        settings.Actions.Add(new("Another tile", "Inspect jobs"));
        await f.Store.SaveSettingsAsync(f.ProjectId, settings);
        await vm.SaveAsync("My private assistant instructions");
        var saved = await f.Store.GetSettingsAsync(f.ProjectId);
        Assert.Equal("My private assistant instructions", saved.Agents[0].Instructions);
        Assert.Contains(saved.Actions, a => a.Title == "Another tile");
        Assert.Empty((await store.GetAsync(f.ProjectId)).Agents);
        f.User.UserId = Guid.NewGuid();
        Assert.Empty((await f.Store.GetSettingsAsync(f.ProjectId)).Agents[0].Instructions);
    }

    [Fact]
    public async Task Workflow_agent_selector_round_trips_the_project_agent_and_rejects_deleted_agents()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        var store = ProjectStore(f);
        var profile = new DashboardAgentProfile { Name = "Reviewer", ConnectionId = f.ConnectionId, Model = "test" };
        await store.SaveAsync(f.ProjectId, new() { Agents = [profile] });
        var vm = new JobChainsViewModel(null!, null!, null!, new AgentNavigation(), modelConnections: f.Connections, projectAgents: store);
        vm.Initialize(f.ProjectId);
        vm.NewChain();
        await vm.OpenLoopActionAt(PlaceContext.Domain.ValueObjects.LoopChainAction.AgentType, 0);
        Assert.Equal(profile.Id, Assert.Single(vm.LoopAgents).Id);
        vm.LoopAgentId = profile.Id;
        vm.SelectLoopAgent();
        vm.LoopInstructions = "Review the output";
        vm.SaveLoopAction();
        Assert.Null(vm.LoopError);
        var stages = vm.BuildCanvasStageViews();
        var action = Assert.IsType<PlaceContext.Domain.ValueObjects.LoopChainAction>(WorkflowStage.FromView(stages[0]).ToStage().Action);
        Assert.Equal(profile.Id, action.AgentId);
        Assert.Equal(f.ConnectionId, action.ConnectionId);
        await vm.EditLoopAction(0);
        Assert.Equal(profile.Id, vm.LoopAgentId);
        var catalog = await store.GetAsync(f.ProjectId);
        catalog.Agents.Clear();
        await store.SaveAsync(f.ProjectId, catalog);
        await vm.EditLoopAction(0);
        vm.SaveLoopAction();
        Assert.NotNull(vm.LoopError);
        Assert.True(vm.ShowLoopEditor);
    }

    private sealed class AgentNavigation : Microsoft.AspNetCore.Components.NavigationManager
    {
        public AgentNavigation() => Initialize("http://localhost/", "http://localhost/");
        protected override void NavigateToCore(string uri, Microsoft.AspNetCore.Components.NavigationOptions options) => Uri = ToAbsoluteUri(uri).AbsoluteUri;
    }
    private static DbProjectAgentStore ProjectStore(Fixture f) => new(f.Provider.GetRequiredService<IServiceScopeFactory>(),
        f.User, f.Tenant, f.Permissions, f.Provider.GetRequiredService<IDataEncryptor>(), f.Connections);

    [Fact]
    public async Task Project_agents_are_shared_encrypted_and_do_not_publish_private_profiles()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        var store = ProjectStore(f);
        await f.Store.SaveSettingsAsync(f.ProjectId, new() { Instructions = "Private instruction" });
        var catalog = new ProjectAgentCatalog { Agents = [new() { Name = "Analyst", Instructions = "Shared instruction" }] };
        await store.SaveAsync(f.ProjectId, catalog);
        Assert.Equal(1, catalog.Version);
        using var scope = f.Provider.CreateScope();
        Assert.DoesNotContain("Shared instruction", (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Projects.SingleAsync()).AgentSettings);
        f.User.UserId = Guid.NewGuid();
        Assert.Equal("Shared instruction", Assert.Single((await store.GetAsync(f.ProjectId)).Agents).Instructions);
        Assert.Empty((await f.Store.GetSettingsAsync(f.ProjectId)).Instructions);
        f.Permissions.Denied = Permission.ProjectsManage;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.SaveAsync(f.ProjectId, catalog));
        Assert.Single((await store.GetAsync(f.ProjectId)).Agents);
        f.Permissions.Denied = null;
        f.Tenant.TenantId = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetAsync(f.ProjectId));
    }

    [Fact]
    public async Task Project_agents_reject_stale_edits_unknown_skills_models_and_out_of_project_mcps()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        var store = ProjectStore(f);
        var catalog = new ProjectAgentCatalog { Agents = [new() { Name = "Analyst" }] };
        await store.SaveAsync(f.ProjectId, catalog);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(f.ProjectId, new()));
        catalog.Agents[0].SkillIds.Add(Guid.NewGuid());
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(f.ProjectId, catalog));
        catalog.Agents[0].SkillIds.Clear();
        catalog.Agents[0].ConnectionId = Guid.NewGuid();
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(f.ProjectId, catalog));
        catalog.Agents[0].ConnectionId = Guid.Empty;
        var foreign = await f.SeedMcpAsync(projectId: Guid.NewGuid());
        catalog.Agents[0].McpConnectionIds.Add(foreign.Id);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(f.ProjectId, catalog));
        Assert.Empty(Assert.Single((await store.GetAsync(f.ProjectId)).Agents).McpConnectionIds);
    }

    [Fact]
    public async Task Project_agent_skills_and_capabilities_reach_prompt_tools_and_model_selection()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        var skill = new AgentSkill { Name = "Report", Instructions = "Write the findings as a concise report." };
        var profile = new DashboardAgentProfile { Name = "Reporter", WorkflowAccess = "none", DataAccess = "none", SkillIds = [skill.Id] };
        var catalog = new ProjectAgentCatalog { Agents = [profile], Skills = [skill] };
        var store = ProjectStore(f);
        await store.SaveAsync(f.ProjectId, catalog);
        var agent = new BuildChatAgent(f.Store, new PlainTextModel(), f.Service, f.Permissions, f.Tenant, projectAgents: store);
        var inspection = await agent.InspectAsync(f.ProjectId, agentId: profile.Id);
        Assert.Contains(skill.Instructions, inspection.Prompt);
        Assert.Contains("Reporter", inspection.Prompt);
        Assert.DoesNotContain("run_workflow", inspection.Tools);
        Assert.DoesNotContain("list_project_tables", inspection.Tools);
        Assert.DoesNotContain("call_mcp_tool", inspection.Tools);
        var tools = new BuildTools(f.ProjectId, f.Service, f.Permissions, f.Store, profile: profile);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.list_jobs(default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.list_artifacts());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.show_live_chart("Denied", sql: "SELECT 1"));
        var context = JsonSerializer.Serialize(await tools.project_context(default));
        Assert.Contains("\"Jobs\":false", context);
        var conversation = new BuildConversation { ProjectId = f.ProjectId, AgentId = profile.Id, ConnectionId = f.ConnectionId, Model = "test" };
        await agent.QueueAsync(conversation, "Use the report skill");
        Assert.Equal(f.ConnectionId, conversation.ConnectionId);
        await agent.RunAsync(conversation, default);
        Assert.Contains(skill.Instructions, conversation.SystemPrompt);
        Assert.Null(conversation.Error);
    }

    [Fact]
    public async Task Skill_edits_and_deletion_preserve_agents_and_remove_assignments()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        var store = ProjectStore(f);
        var skill = new AgentSkill { Name = "Report", Instructions = "Original" };
        await store.SaveAsync(f.ProjectId, new() { Skills = [skill], Agents = [new() { Name = "Reporter", SkillIds = [skill.Id] }] });
        var vm = new ProjectAgentsViewModel(store, f.Connections, new EmptyMcp(), f.Permissions, new());
        await vm.LoadAsync(f.ProjectId);
        vm.EditSkill(vm.Catalog.Skills[0]);
        await vm.SaveAsync("Edited instructions");
        Assert.Null(vm.Error);
        Assert.Equal("Edited instructions", (await store.GetAsync(f.ProjectId)).Skills[0].Instructions);
        vm.EditSkill(vm.Catalog.Skills[0]);
        vm.ConfirmDelete = true;
        await vm.SaveAsync("", true);
        Assert.Empty(vm.Catalog.Skills);
        Assert.Empty(Assert.Single(vm.Catalog.Agents).SkillIds);
    }

    private sealed class EmptyMcp : PlaceContext.Application.Mcp.IMcpClientService
    {
        public Task<IReadOnlyList<PlaceContext.Application.Mcp.McpConnectionInfo>> ListConnectionsAsync(Guid id, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PlaceContext.Application.Mcp.McpConnectionInfo>>([]);
        public Task<IReadOnlyList<PlaceContext.Application.Mcp.McpToolDefinition>> ListToolsAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PlaceContext.Application.Mcp.McpToolResult> CallToolAsync(Guid id, string name, JsonElement? args = null, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
