using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Build;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Secrets;
using PlaceContext.Host.Components.ViewModels;
using PlaceContext.Infrastructure.Build;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Host.Tests;

public sealed partial class BuildChatTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Parent_approval_is_required_in_dashboard_chat(bool approve)
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        var parent = new DashboardAgentProfile { Name = "Supervisor", Instructions = "Check the task scope" };
        var child = new DashboardAgentProfile { Name = "Worker", ParentAgentId = parent.Id };
        var store = ProjectStore(f);
        await store.SaveAsync(f.ProjectId, new() { Agents = [parent, child] });
        Assert.Equal(parent.Id, (await store.GetAsync(f.ProjectId)).Agents.Single(a => a.Id == child.Id).ParentAgentId);
        var catalog = await store.GetAsync(f.ProjectId);
        catalog.Agents.RemoveAll(a => a.Id == parent.Id);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(f.ProjectId, catalog));
        var model = new ApprovalChatModel(approve);
        var agent = new BuildChatAgent(f.Store, model, f.Service, f.Permissions, f.Tenant, projectAgents: store);
        var conversation = new BuildConversation { ProjectId = f.ProjectId, AgentId = child.Id, ConnectionId = f.ConnectionId, Model = "test" };
        await agent.QueueAsync(conversation, "Explain the project");
        await agent.RunAsync(conversation, default);
        Assert.Equal(approve ? 1 : 0, model.ChildCalls);
        Assert.Equal(approve ? 2 : 1, model.Approvals);
        if (approve) Assert.Null(conversation.Error);
        else Assert.Contains("Supervisor rejected approval", conversation.Error);
    }

    private sealed class ApprovalChatModel(bool approve) : IBuildModelGateway
    {
        public int ChildCalls, Approvals;
        public Task<JsonObject> CompleteAsync(Guid connectionId, string model, JsonArray messages, JsonArray tools, CancellationToken ct, Action<string>? report = null)
        {
            var prompt = messages[0]!["content"]!.ToString();
            if (prompt.StartsWith("You are the parent agent responsible"))
            {
                Approvals++;
                Assert.Empty(tools);
                Assert.Contains("Supervisor", prompt);
                return Task.FromResult(new JsonObject { ["content"] = JsonSerializer.Serialize(new { approved = approve, reason = "Task scope reviewed" }) });
            }
            ChildCalls++;
            return Task.FromResult(new JsonObject { ["content"] = "Project explanation" });
        }
    }

    [Fact]
    public async Task Dashboard_master_discovers_project_agents_without_gaining_additional_permissions()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        var profile = new DashboardAgentProfile { Name = "Report reviewer", SourceAccess = "none" };
        var store = ProjectStore(f);
        await store.SaveAsync(f.ProjectId, new() { Agents = [profile] });
        var settings = await f.Store.GetSettingsAsync(f.ProjectId);
        settings.Agents[0].WorkflowAccess = "read";
        settings.Agents[0].TableAccess = "none";
        await f.Store.SaveSettingsAsync(f.ProjectId, settings);
        var agent = new BuildChatAgent(f.Store, new PlainTextModel(), f.Service, f.Permissions, f.Tenant, projectAgents: store);
        var inspection = await agent.InspectAsync(f.ProjectId);
        Assert.Contains("You are the dashboard master agent", inspection.Prompt);
        Assert.Contains("Review.AgentId", inspection.Prompt);
        Assert.Contains(profile.Id.ToString(), inspection.Prompt);
        Assert.Contains(profile.Name, inspection.Prompt);
        Assert.Contains("\"SourceAccess\":\"none\"", inspection.Prompt);
        var toolNames = JsonSerializer.Deserialize<JsonElement>(inspection.Tools).EnumerateArray().Select(t => t.GetProperty("Name").GetString()).ToArray();
        Assert.DoesNotContain("run_workflow", toolNames);
        Assert.DoesNotContain("save_workflow", toolNames);
        Assert.DoesNotContain("list_project_tables", toolNames);
        inspection = await agent.InspectAsync(f.ProjectId, agentId: profile.Id);
        Assert.DoesNotContain("You are the dashboard master agent", inspection.Prompt);
    }

    [Fact]
    public async Task Agent_routes_open_the_requested_agent_and_fail_closed_for_missing_agents()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        var profile = new DashboardAgentProfile { Name = "Linked agent" };
        var store = ProjectStore(f);
        await store.SaveAsync(f.ProjectId, new() { Agents = [profile] });
        var vm = new ProjectAgentsViewModel(store, f.Connections, new EmptyMcp(), f.Permissions, new(), f.Store);
        await vm.LoadPageAsync(f.ProjectId, profile.Id.ToString());
        Assert.Equal(profile.Id, vm.Agent!.Id);
        Assert.False(vm.EditingPrivateAgent);
        await vm.LoadPageAsync(f.ProjectId, Guid.Empty.ToString());
        Assert.True(vm.EditingPrivateAgent);
        Assert.Equal(DashboardSettings.DefaultAgentId, vm.Agent!.Id);
        await vm.LoadPageAsync(f.ProjectId, DashboardSettings.DefaultAgentId.ToString());
        Assert.True(vm.EditingPrivateAgent);
        Assert.False(vm.CanDeleteAgent(vm.Agent!));
        await vm.LoadPageAsync(f.ProjectId, "skills");
        Assert.False(vm.EditorOpen);
        Assert.Equal("Agent not found in this project.", vm.Error);
        await vm.LoadPageAsync(f.ProjectId, Guid.NewGuid().ToString());
        Assert.False(vm.EditorOpen);
        Assert.Equal("Agent not found in this project.", vm.Error);
        await vm.LoadPageAsync(f.ProjectId, "new");
        Assert.NotNull(vm.Agent);
        f.Permissions.Denied = Permission.ProjectsManage;
        await vm.LoadPageAsync(f.ProjectId, "new");
        Assert.False(vm.EditorOpen);
        Assert.NotNull(vm.Error);
    }

    [Fact]
    public async Task Navigating_during_agent_loading_keeps_the_latest_route()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        var pending = new TaskCompletionSource<IReadOnlyList<JobChainView>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = new ToolDispatcher(f.ProjectId) { PendingWorkflows = pending };
        var service = new PlaceContextService(dispatcher, null!, null!, null!);
        var vm = new ProjectAgentsViewModel(ProjectStore(f), f.Connections, new EmptyMcp(), f.Permissions, new(), f.Store, service);
        var first = vm.LoadPageAsync(f.ProjectId, Guid.Empty.ToString());
        var second = vm.LoadPageAsync(f.ProjectId, "skills");
        var latest = vm.LoadPageAsync(f.ProjectId, "new");
        pending.SetResult([]);
        await Task.WhenAll(first, second, latest);
        Assert.NotNull(vm.Agent);
        Assert.NotEqual(Guid.Empty, vm.Agent.Id);
        Assert.False(vm.Existing);
        Assert.Null(vm.Error);
    }

    [Theory]
    [InlineData("none", "none", "none")]
    [InlineData("read", "read", "read")]
    [InlineData("manage", "read", "manage")]
    public void Legacy_agents_keep_their_data_permissions(string legacy, string read, string sources)
    {
        var profile = JsonSerializer.Deserialize<DashboardAgentProfile>("{\"DataAccess\":\"" + legacy + "\"}")!;
        profile.Validate();
        Assert.Equal(read, profile.EffectiveTableAccess);
        Assert.Equal(read, profile.EffectiveArtifactAccess);
        Assert.Equal(sources, profile.EffectiveSourceAccess);
        Assert.Equal(read == "read", profile.AllowsTool("list_project_tables"));
        Assert.Equal(sources == "manage", profile.AllowsTool("prepare_data_source"));
    }

    [Theory]
    [InlineData("metadata", "list_data_sources")]
    [InlineData("search", "search_data_sources")]
    [InlineData("connect", "prepare_data_source")]
    [InlineData("pipeline", "create_data_pipeline")]
    public void Source_capabilities_are_independent_and_preserved_in_json(string capability, string tool)
    {
        var profile = new DashboardAgentProfile { SourceAccess = "none", SourceCapabilities = [capability] };
        profile = JsonSerializer.Deserialize<DashboardAgentProfile>(JsonSerializer.Serialize(profile))!;
        profile.Validate();
        foreach (var name in new[] { "list_data_sources", "search_data_sources", "prepare_data_source", "create_data_pipeline" })
            Assert.Equal(name == tool, profile.AllowsTool(name));
        profile.WorkflowAccess = "read";
        Assert.False(profile.AllowsTool("create_data_pipeline"));
        profile.SourceCapabilities = [];
        profile.SourceAccess = "manage";
        Assert.False(profile.AllowsTool(tool));
    }

    [Fact]
    public void Invalid_source_capabilities_are_rejected()
    {
        foreach (var capabilities in new List<string>[] { ["unknown"], ["search", "search"], [null!] })
            Assert.Throws<ArgumentException>(() => new DashboardAgentProfile { SourceCapabilities = capabilities }.Validate());
    }

    [Fact]
    public async Task Metadata_only_agent_cannot_search_connect_or_create_pipelines_through_direct_calls()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        var profile = new DashboardAgentProfile { SourceCapabilities = ["metadata"] };
        var service = new PlaceContextService(new ToolDispatcher(f.ProjectId), null!, null!, null!);
        var tools = new BuildTools(f.ProjectId, service, f.Permissions, f.Store, profile: profile);
        Assert.Contains(tools.Functions(), t => t.Name == "list_data_sources");
        Assert.DoesNotContain(tools.Functions(), t => t.Name is "search_data_sources" or "prepare_data_source" or "create_data_pipeline");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.search_data_sources("private records", 10, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.prepare_data_source("postgres", default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.create_data_pipeline(Guid.NewGuid(), "https://example.test", default));
        f.Permissions.Denied = Permission.DataRead;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.list_data_sources(default));
    }

    [Theory]
    [InlineData("manage", "read", "read")]
    [InlineData("read", "invalid", "read")]
    [InlineData("read", "read", "manage")]
    public void Invalid_separate_data_permissions_are_rejected(string tables, string sources, string artifacts)
    {
        var profile = new DashboardAgentProfile { TableAccess = tables, SourceAccess = sources, ArtifactAccess = artifacts };
        Assert.Throws<ArgumentException>(profile.Validate);
    }

    [Fact]
    public async Task Separate_data_permissions_gate_tools_and_direct_calls_without_disabling_workflow_charts()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        var dispatcher = new ToolDispatcher(f.ProjectId);
        var service = new PlaceContextService(dispatcher, null!, null!, null!);
        var profile = new DashboardAgentProfile { TableAccess = "none", SourceAccess = "none", ArtifactAccess = "none" };
        var tools = new BuildTools(f.ProjectId, service, f.Permissions, f.Store, profile: profile);
        Assert.DoesNotContain(tools.Functions(), t => t.Name is "list_project_tables" or "source_connectors" or "list_data_sources"
            or "search_data_sources" or "prepare_data_source" or "create_data_pipeline" or "list_artifacts");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.list_project_tables(default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.list_artifacts());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.source_connectors(default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.list_data_sources(default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.search_data_sources("records", 10, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.prepare_data_source("postgres", default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.create_data_pipeline(Guid.NewGuid(), "http://localhost", default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.show_live_chart("Denied", sql: "SELECT 1"));
        Assert.Equal(0, dispatcher.DataQueries);
        await tools.show_live_chart("Workflow counts");

        profile.TableAccess = "read";
        profile.DataAccess = "none";
        await tools.list_project_tables(default);
        await tools.show_live_chart("Allowed", sql: "SELECT status, total FROM records");
        Assert.Equal(1, dispatcher.DataQueries);
        Assert.False(profile.AllowsTool("search_data_sources"));
        Assert.False(profile.AllowsTool("list_artifacts"));
        f.Permissions.Denied = Permission.DataRead;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.list_project_tables(default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.show_live_chart("Denied by user permissions", sql: "SELECT 1"));
        Assert.Equal(1, dispatcher.DataQueries);

        profile.SourceAccess = "read";
        Assert.True(profile.AllowsTool("search_data_sources"));
        Assert.False(profile.AllowsTool("prepare_data_source"));
        profile.SourceAccess = "manage";
        Assert.True(profile.AllowsTool("prepare_data_source"));
        Assert.True(profile.AllowsTool("create_data_pipeline"));
        profile.WorkflowAccess = "read";
        Assert.False(profile.AllowsTool("create_data_pipeline"));
        profile.ArtifactAccess = "read";
        await tools.list_artifacts();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Agent_workflow_links_save_reopen_and_can_remove_deleted_workflows(bool privateAgent)
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        var workflow = new JobChainView(Guid.NewGuid(), f.ProjectId, "Review report", null, [], DateTimeOffset.UtcNow);
        using var scope = f.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.JobChains.Add(new() { Id = workflow.Id, ProjectId = f.ProjectId, Name = workflow.Name });
        await db.SaveChangesAsync();
        var dispatcher = new ToolDispatcher(f.ProjectId) { Workflow = workflow };
        var service = new PlaceContextService(dispatcher, null!, null!, null!);
        var store = ProjectStore(f);
        var vm = new ProjectAgentsViewModel(store, f.Connections, new EmptyMcp(), f.Permissions, new(), f.Store, service);
        await vm.LoadAsync(f.ProjectId);
        vm.EditAgent(privateAgent ? Assert.Single(vm.Agents) : null);
        vm.Agent!.Name = "Reviewer";
        vm.Agent.TableAccess = "none";
        vm.Agent.SourceAccess = "read";
        vm.ToggleSourceCapability("search", false);
        vm.ToggleSourceCapability("pipeline", true);
        vm.ToggleSourceCapability("pipeline", true);
        vm.ToggleSourceCapability("invalid", true);
        vm.Agent.ArtifactAccess = "none";
        var id = vm.Agent.Id;
        vm.ToggleWorkflow(Guid.NewGuid(), true);
        Assert.Empty(vm.Agent.WorkflowIds);
        vm.ToggleWorkflow(workflow.Id, true);
        vm.ToggleWorkflow(workflow.Id, true);
        Assert.Equal(workflow.Id, Assert.Single(vm.Agent.WorkflowIds));
        await vm.SaveAsync("Review linked workflows");
        Assert.Null(vm.Error);
        await vm.LoadAsync(f.ProjectId);
        vm.EditAgent(vm.Agents.Single(a => a.Id == id));
        Assert.Equal(workflow.Id, Assert.Single(vm.Agent!.WorkflowIds));
        Assert.Equal("none", vm.Agent.EffectiveTableAccess);
        Assert.Equal("read", vm.Agent.EffectiveSourceAccess);
        Assert.Equal(new[] { "metadata", "pipeline" }, vm.Agent.EffectiveSourceCapabilities);
        Assert.Equal("none", vm.Agent.EffectiveArtifactAccess);
        if (privateAgent) Assert.Empty((await store.GetAsync(f.ProjectId)).Agents);

        var agent = new BuildChatAgent(f.Store, new PlainTextModel(), service, f.Permissions, f.Tenant, projectAgents: store);
        var inspection = await agent.InspectAsync(f.ProjectId, agentId: id);
        Assert.Contains("\"LinkedWorkflows\":[{\"Id\":\"" + workflow.Id, inspection.Prompt);
        Assert.Contains(workflow.Name, inspection.Prompt);

        db.JobChains.Remove(await db.JobChains.SingleAsync());
        await db.SaveChangesAsync();
        dispatcher.Workflow = null;
        await vm.SaveAsync("An unrelated change after deleting the workflow");
        Assert.Null(vm.Error);
        await vm.LoadAsync(f.ProjectId);
        vm.EditAgent(vm.Agents.Single(a => a.Id == id));
        Assert.Empty(vm.Workflows);
        Assert.Single(vm.Agent!.WorkflowIds);
        vm.ToggleWorkflow(workflow.Id, false);
        await vm.SaveAsync("Review linked workflows");
        Assert.Null(vm.Error);
        await vm.LoadAsync(f.ProjectId);
        Assert.Empty(vm.Agents.Single(a => a.Id == id).WorkflowIds);
    }

    [Fact]
    public async Task Agent_workflow_links_reject_invalid_ids_and_other_projects_in_both_stores()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        var workflowId = Guid.NewGuid();
        using var scope = f.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.JobChains.Add(new() { Id = workflowId, ProjectId = Guid.NewGuid(), Name = "Foreign workflow" });
        await db.SaveChangesAsync();
        var settings = new DashboardSettings();
        var profile = new DashboardAgentProfile();
        var catalog = new ProjectAgentCatalog { Agents = [profile] };
        foreach (var ids in new List<Guid>[] { [workflowId], [Guid.NewGuid()], [Guid.Empty], [workflowId, workflowId] })
        {
            profile.WorkflowIds = ids;
            settings.Agents[0].WorkflowIds = ids;
            await Assert.ThrowsAsync<ArgumentException>(() => ProjectStore(f).SaveAsync(f.ProjectId, catalog));
            await Assert.ThrowsAsync<ArgumentException>(() => f.Store.SaveSettingsAsync(f.ProjectId, settings));
        }
        Assert.Empty((await ProjectStore(f).GetAsync(f.ProjectId)).Agents);
        Assert.Empty((await f.Store.GetSettingsAsync(f.ProjectId)).Agents[0].WorkflowIds);
    }

    [Fact]
    public async Task Linked_workflow_context_respects_capabilities_permissions_and_current_project_workflows()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        var workflow = new JobChainView(Guid.NewGuid(), f.ProjectId, "Report", null, [], DateTimeOffset.UtcNow);
        var dispatcher = new ToolDispatcher(f.ProjectId) { Workflow = workflow };
        var service = new PlaceContextService(dispatcher, null!, null!, null!);
        var profile = new DashboardAgentProfile { WorkflowIds = [workflow.Id, Guid.NewGuid()] };
        var tools = new BuildTools(f.ProjectId, service, f.Permissions, f.Store, profile: profile);
        var context = JsonSerializer.SerializeToElement(await tools.project_context(default));
        Assert.Equal(workflow.Id, Assert.Single(context.GetProperty("LinkedWorkflows").EnumerateArray()).GetProperty("Id").GetGuid());
        profile.WorkflowAccess = "none";
        context = JsonSerializer.SerializeToElement(await tools.project_context(default));
        Assert.Empty(context.GetProperty("LinkedWorkflows").EnumerateArray());
        profile.WorkflowAccess = "manage";
        f.Permissions.Denied = Permission.ChainsManage;
        context = JsonSerializer.SerializeToElement(await tools.project_context(default));
        Assert.Empty(context.GetProperty("LinkedWorkflows").EnumerateArray());
        var vm = new ProjectAgentsViewModel(ProjectStore(f), f.Connections, new EmptyMcp(), f.Permissions, new(), f.Store, service);
        await vm.LoadAsync(f.ProjectId);
        Assert.False(vm.CanViewWorkflows);
        Assert.Empty(vm.Workflows);
        vm.EditAgent();
        vm.ToggleWorkflow(workflow.Id, true);
        Assert.Empty(vm.Agent!.WorkflowIds);
    }

    [Fact]
    public async Task Agents_section_edits_the_private_dashboard_agent_without_publishing_or_replacing_tiles()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        var store = ProjectStore(f);
        var vm = new ProjectAgentsViewModel(store, f.Connections, new EmptyMcp(), f.Permissions, new(), f.Store);
        await vm.LoadAsync(f.ProjectId);
        var dashboard = Assert.Single(vm.Agents);
        Assert.Equal(DashboardSettings.DefaultAgentId, dashboard.Id);
        vm.EditAgent(dashboard);
        Assert.True(vm.EditingPrivateAgent);
        Assert.False(vm.CanDeleteAgent(vm.Agent!));
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

    [Theory]
    [InlineData(PlaceContext.Domain.ValueObjects.LoopChainAction.AgentType)]
    [InlineData(PlaceContext.Domain.ValueObjects.LoopChainAction.TaskType)]
    public async Task Workflow_steps_list_and_run_dashboard_and_private_agents(string kind)
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        var settings = await f.Store.GetSettingsAsync(f.ProjectId);
        var personal = new DashboardAgentProfile { Name = "Private reviewer", Instructions = "Check personal evidence.",
            ConnectionId = f.ConnectionId, Model = "test" };
        settings.Agents.Add(personal);
        await f.Store.SaveSettingsAsync(f.ProjectId, settings);
        var shared = new DashboardAgentProfile { Name = "Shared reviewer" };
        var store = ProjectStore(f);
        await store.SaveAsync(f.ProjectId, new() { Agents = [shared] });
        var vm = new JobChainsViewModel(null!, null!, null!, new AgentNavigation(), modelConnections: f.Connections,
            projectAgents: store, dashboardStore: f.Store);
        vm.Initialize(f.ProjectId);
        vm.NewChain();
        await vm.OpenLoopActionAt(kind, 0);
        Assert.Equal(3, vm.LoopAgents.Count);
        Assert.Contains(vm.LoopAgents, a => a.Id == DashboardSettings.DefaultAgentId);
        Assert.Contains(vm.LoopAgents, a => a.Id == shared.Id);
        foreach (var profile in settings.Agents)
        {
            vm.LoopAgentId = profile.Id;
            vm.SelectLoopAgent();
            vm.LoopInstructions = "Review the output";
            vm.SaveLoopAction();
            Assert.Null(vm.LoopError);
            var action = Assert.IsType<PlaceContext.Domain.ValueObjects.LoopChainAction>(WorkflowStage.FromView(vm.BuildCanvasStageViews()[0]).ToStage().Action);
            Assert.Equal(profile.Id, action.AgentId);
            var model = new ScriptedModel("""{"content":"{\"done\":true}"}""");
            var runner = new WorkflowAgentRunner(model, f.Connections, f.Service, f.Permissions, f.Store, projectAgents: store);
            await runner.RunAsync(f.ProjectId, Guid.NewGuid(), Guid.NewGuid(), 0, action, null, default);
            if (kind == PlaceContext.Domain.ValueObjects.LoopChainAction.TaskType)
            {
                Assert.Contains(profile.Name, model.SystemPrompt);
                Assert.Contains(profile.Instructions, model.SystemPrompt);
            }
            else Assert.Null(model.SystemPrompt);
            await vm.EditLoopAction(0);
            Assert.Equal(profile.Id, vm.LoopAgentId);
        }
        f.User.UserId = Guid.NewGuid();
        var unavailable = new PlaceContext.Domain.ValueObjects.LoopChainAction(kind, "Review", "review",
            agentProvider: PlaceContext.Domain.ValueObjects.LoopChainAction.AutomaticAgent,
            connectionId: f.ConnectionId, model: "test", agentId: personal.Id);
        var deniedModel = new ScriptedModel();
        var deniedRunner = new WorkflowAgentRunner(deniedModel, f.Connections, f.Service, f.Permissions, f.Store, projectAgents: store);
        await Assert.ThrowsAsync<InvalidOperationException>(() => deniedRunner.RunAsync(f.ProjectId, Guid.NewGuid(), Guid.NewGuid(), 0, unavailable, null, default));
        Assert.Equal(0, deniedModel.Requests);
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
    public async Task Project_agents_reject_stale_edits_unknown_models_and_out_of_project_mcps()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        var store = ProjectStore(f);
        var catalog = new ProjectAgentCatalog { Agents = [new() { Name = "Analyst" }] };
        await store.SaveAsync(f.ProjectId, catalog);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(f.ProjectId, new()));
        catalog.Agents[0].ConnectionId = Guid.NewGuid();
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(f.ProjectId, catalog));
        catalog.Agents[0].ConnectionId = Guid.Empty;
        var foreign = await f.SeedMcpAsync(projectId: Guid.NewGuid());
        catalog.Agents[0].McpConnectionIds.Add(foreign.Id);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(f.ProjectId, catalog));
        Assert.Empty(Assert.Single((await store.GetAsync(f.ProjectId)).Agents).McpConnectionIds);
    }

    [Fact]
    public async Task Project_agent_capabilities_reach_prompt_tools_and_model_selection()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        var profile = new DashboardAgentProfile { Name = "Reporter", WorkflowAccess = "none", DataAccess = "none" };
        var catalog = new ProjectAgentCatalog { Agents = [profile] };
        var store = ProjectStore(f);
        await store.SaveAsync(f.ProjectId, catalog);
        var agent = new BuildChatAgent(f.Store, new PlainTextModel(), f.Service, f.Permissions, f.Tenant, projectAgents: store);
        var inspection = await agent.InspectAsync(f.ProjectId, agentId: profile.Id);
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
        await agent.QueueAsync(conversation, "Use the report agent");
        Assert.Equal(f.ConnectionId, conversation.ConnectionId);
        await agent.RunAsync(conversation, default);
        Assert.Contains("Reporter", conversation.SystemPrompt);
        Assert.Null(conversation.Error);
    }

    [Fact]
    public async Task Deleting_an_agent_removes_only_that_agent_and_keeps_the_master()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        var store = ProjectStore(f);
        var keep = new DashboardAgentProfile { Name = "Keep" };
        var remove = new DashboardAgentProfile { Name = "Remove" };
        await store.SaveAsync(f.ProjectId, new() { Agents = [keep, remove] });
        var vm = new ProjectAgentsViewModel(store, f.Connections, new EmptyMcp(), f.Permissions, new(), f.Store);
        await vm.LoadAsync(f.ProjectId);
        var master = vm.Agents.Single(a => a.Id == DashboardSettings.DefaultAgentId);
        Assert.False(vm.CanDeleteAgent(master));
        await vm.DeleteAsync(master.Id);
        Assert.Contains(vm.Agents, a => a.Id == DashboardSettings.DefaultAgentId);
        await vm.DeleteAsync(remove.Id);
        Assert.Null(vm.Error);
        Assert.DoesNotContain(vm.Agents, a => a.Id == remove.Id);
        var saved = await store.GetAsync(f.ProjectId);
        Assert.Equal(keep.Id, Assert.Single(saved.Agents).Id);
    }

    private sealed class EmptyMcp : PlaceContext.Application.Mcp.IMcpClientService
    {
        public Task<IReadOnlyList<PlaceContext.Application.Mcp.McpConnectionInfo>> ListConnectionsAsync(Guid id, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PlaceContext.Application.Mcp.McpConnectionInfo>>([]);
        public Task<IReadOnlyList<PlaceContext.Application.Mcp.McpToolDefinition>> ListToolsAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PlaceContext.Application.Mcp.McpToolResult> CallToolAsync(Guid id, string name, JsonElement? args = null, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
