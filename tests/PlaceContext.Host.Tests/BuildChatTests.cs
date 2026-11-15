using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Artifacts;
using PlaceContext.Application.Build;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Jobs;
using PlaceContext.Application.Membership;
using PlaceContext.Application.Mcp;
using PlaceContext.Application.Operations;
using PlaceContext.Application.Secrets;
using PlaceContext.Application.Tenancy;
using PlaceContext.Application;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Host.Components.ViewModels;
using PlaceContext.Infrastructure.Build;
using PlaceContext.Infrastructure.Mcp;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Security;
using PlaceContext.Infrastructure.Tenancy;
using PlaceContext.Application.Access;

namespace PlaceContext.Host.Tests;

public sealed partial class BuildChatTests
{
    [Fact]
    public async Task Editing_a_message_preserves_the_original_and_queues_a_separate_conversation()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var agent = new BuildChatAgent(fixture.Store, new PlainTextModel(), fixture.Service, fixture.Permissions, fixture.Tenant);
        var original = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test" };
        await agent.QueueAsync(original, "Explore revenue");
        await agent.RunAsync(original, default);
        await agent.QueueAsync(original, "Show the last year");
        await agent.RunAsync(original, default);
        var saved = original.Messages.ToJsonString();
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, agent, new(), new NoJs(), fixture.Service, fixture.Permissions);
        await vm.LoadAsync(fixture.ProjectId, original.Id);
        vm.EditMessage(vm.Conversation.Messages[2]!);
        Assert.True(vm.BranchPending);
        Assert.Equal("Show the last year", vm.Draft);
        Assert.Single(await fixture.Store.ListAsync(fixture.ProjectId));
        vm.Draft = "Show the last quarter";
        await vm.SendAsync();
        Assert.Null(vm.Error);
        Assert.False(vm.BranchPending);
        Assert.NotEqual(original.Id, vm.Conversation.Id);
        Assert.Equal(original.Id, vm.Conversation.ParentConversationId);
        Assert.Equal(2, vm.Conversation.ParentMessageCount);
        Assert.Equal(BuildRunStatus.Queued, vm.Conversation.Status);
        Assert.Equal(3, vm.Conversation.Messages.Count);
        Assert.Equal("Show the last quarter", BuildChatViewModel.Text(vm.Conversation.Messages.Last()!));
        Assert.Equal(saved, (await fixture.Store.LoadAsync(fixture.ProjectId, original.Id)).Messages.ToJsonString());
        var branch = await fixture.Store.LoadAsync(fixture.ProjectId, vm.Conversation.Id);
        Assert.Equal(original.Id, branch.ParentConversationId);
        Assert.Equal(2, branch.ParentMessageCount);
        await vm.DisposePageAsync();
    }

    [Fact]
    public async Task Retry_requires_send_and_branch_retains_complete_tool_history()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var agent = new BuildChatAgent(fixture.Store, new ScriptedModel(), fixture.Service, fixture.Permissions, fixture.Tenant);
        var original = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test" };
        await agent.QueueAsync(original, "Explain job contracts");
        await agent.RunAsync(original, default);
        Assert.Equal(4, original.Messages.Count);
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, agent, new(), new NoJs(), fixture.Service, fixture.Permissions);
        await vm.LoadAsync(fixture.ProjectId, original.Id);
        var reply = vm.Conversation.Messages.Last()!;
        vm.RetryMessage(reply);
        Assert.Equal("Explain job contracts", vm.Draft);
        Assert.False(vm.Busy);
        Assert.Single(await fixture.Store.ListAsync(fixture.ProjectId));
        vm.CancelBranch();
        Assert.False(vm.BranchPending);
        Assert.Empty(vm.Draft);
        Assert.False(vm.CanBranchMessage(vm.Conversation.Messages[1]!));
        vm.BranchFromMessage(reply);
        vm.Draft = "Explain this in more detail";
        await vm.SendAsync();
        Assert.Null(vm.Error);
        Assert.Equal(5, vm.Conversation.Messages.Count);
        Assert.Equal(original.Messages.ToJsonString(), new JsonArray(vm.Conversation.Messages.Take(4).Select(x => x!.DeepClone()).ToArray()).ToJsonString());
        Assert.Equal(4, vm.Conversation.ParentMessageCount);
        Assert.False(vm.CanRetryMessage(vm.Conversation.Messages[3]!));
        await vm.DisposePageAsync();
    }

    [Fact]
    public async Task Retrying_the_first_turn_queues_only_the_original_prompt_and_preserves_completed_actions()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var agent = new BuildChatAgent(fixture.Store, new ScriptedModel(), fixture.Service, fixture.Permissions, fixture.Tenant);
        var original = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test" };
        await agent.QueueAsync(original, "Explain job contracts");
        await agent.RunAsync(original, default);
        var originalMessages = original.Messages.ToJsonString();
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, agent, new(), new NoJs(), fixture.Service, fixture.Permissions);
        await vm.LoadAsync(fixture.ProjectId, original.Id);
        vm.RetryMessage(vm.Conversation.Messages.Last()!);
        await vm.SendAsync();
        Assert.Null(vm.Error);
        Assert.Equal("Explain job contracts", BuildChatViewModel.Text(Assert.Single(vm.Conversation.Messages)!));
        Assert.Equal(BuildRunStatus.Queued, vm.Conversation.Status);
        Assert.Equal(0, vm.Conversation.ParentMessageCount);
        Assert.Equal(originalMessages, (await fixture.Store.LoadAsync(fixture.ProjectId, original.Id)).Messages.ToJsonString());
        await vm.DisposePageAsync();
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("other-user")]
    [InlineData("running")]
    public async Task Branch_send_rechecks_ownership_existence_and_active_runs(string change)
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var agent = new BuildChatAgent(fixture.Store, new PlainTextModel(), fixture.Service, fixture.Permissions, fixture.Tenant);
        var original = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test" };
        await agent.QueueAsync(original, "Inspect revenue");
        await agent.RunAsync(original, default);
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, agent, new(), new NoJs(), fixture.Service, fixture.Permissions);
        await vm.LoadAsync(fixture.ProjectId, original.Id);
        vm.RetryMessage(vm.Conversation.Messages.Last()!);
        if (change == "deleted")
        {
            await fixture.Store.DeleteAsync(fixture.ProjectId, original.Id);
        }
        if (change == "other-user")
        {
            fixture.User.UserId = Guid.NewGuid();
        }
        if (change == "running")
        {
            await agent.QueueAsync(original, "A turn in another tab");
        }
        await vm.SendAsync();
        Assert.NotNull(vm.Error);
        Assert.True(vm.BranchPending);
        Assert.Equal(original.Id, vm.Conversation.Id);
        Assert.Equal("Inspect revenue", vm.Draft);
        Assert.DoesNotContain(await fixture.Store.ListAsync(fixture.ProjectId), chat => chat.Id != original.Id);
        await vm.DisposePageAsync();
    }

    [Fact]
    public async Task Chat_actions_preserve_unsent_drafts_and_filter_recent_titles_without_case_sensitivity()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var agent = new BuildChatAgent(fixture.Store, new PlainTextModel(), fixture.Service, fixture.Permissions, fixture.Tenant);
        var original = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test" };
        await agent.QueueAsync(original, "Explore revenue");
        await agent.RunAsync(original, default);
        var js = new ClipboardJs();
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, agent, new(), js, fixture.Service, fixture.Permissions);
        await vm.LoadAsync(fixture.ProjectId, original.Id);
        vm.HistoryQuery = "  REVENUE  ";
        Assert.Equal(original.Id, Assert.Single(vm.FilteredConversations).Id);
        vm.HistoryQuery = "unmatched";
        Assert.Empty(vm.FilteredConversations);
        vm.HistoryQuery = "";
        Assert.Single(vm.FilteredConversations);
        vm.Draft = "Keep this draft";
        vm.EditMessage(vm.Conversation.Messages[0]!);
        vm.RetryMessage(vm.Conversation.Messages.Last()!);
        vm.BranchFromMessage(vm.Conversation.Messages.Last()!);
        Assert.False(vm.BranchPending);
        Assert.Equal("Keep this draft", vm.Draft);
        await vm.CopyMessageAsync(vm.Conversation.Messages[0]!);
        Assert.Equal("Explore revenue", js.Copied);
        js.Fail = true;
        await vm.CopyMessageAsync(vm.Conversation.Messages.Last()!);
        Assert.Contains("Clipboard access is unavailable", vm.Notice);
        vm.Draft = "";
        vm.RetryMessage(vm.Conversation.Messages.Last()!);
        await vm.NewAsync();
        Assert.False(vm.BranchPending);
        Assert.Empty(vm.Draft);
        await vm.DisposePageAsync();
    }

    private sealed class ClipboardJs : IJSRuntime
    {
        public string? Copied;
        public bool Fail;
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Assert.Equal("navigator.clipboard.writeText", identifier);
            if (Fail)
            {
                throw new JSException("Denied");
            }
            Copied = args![0]?.ToString();
            return ValueTask.FromResult(default(TValue)!);
        }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    [Theory]
    [InlineData("Assistant", "Master agent")]
    [InlineData("Dashboard agent", "Master agent")]
    [InlineData("My coordinator", "My coordinator")]
    public void Master_agent_rename_preserves_custom_configuration(string name, string expected)
    {
        var settings = new DashboardSettings();
        settings.Agents[0].Name = name;
        settings.Agents[0].Instructions = "Keep the project context.";
        var restored = JsonSerializer.Deserialize<DashboardSettings>(JsonSerializer.Serialize(settings))!;
        var master = Assert.Single(restored.Agents);
        Assert.Equal(expected, master.Name);
        Assert.Equal("Master agent", master.DisplayName);
        Assert.Equal("Keep the project context.", master.Instructions);
        Assert.Equal("Researcher", new DashboardAgentProfile { Name = "Researcher" }.DisplayName);
    }

    [Fact]
    public void Default_agent_identity_preserves_legacy_settings_and_conversations()
    {
        var settings = JsonSerializer.Deserialize<DashboardSettings>("""
            {"Agents":[{"Id":"00000000-0000-0000-0000-000000000000","Name":"My assistant","Instructions":"Keep my preferences"}]}
            """)!;
        var profile = Assert.Single(settings.Agents);
        Assert.NotEqual(Guid.Empty, profile.Id);
        Assert.Equal(DashboardSettings.DefaultAgentId, profile.Id);
        Assert.Equal("Keep my preferences", profile.Instructions);
        settings.Validate();
        var reloaded = JsonSerializer.Deserialize<DashboardSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(profile.Id, Assert.Single(reloaded.Agents).Id);
        var conversation = JsonSerializer.Deserialize<BuildConversation>("""{"AgentId":"00000000-0000-0000-0000-000000000000"}""")!;
        Assert.Equal(profile.Id, conversation.AgentId);
        Assert.Throws<ArgumentException>(() => new ProjectAgentCatalog { Agents = [profile] }.Validate());
    }

    [Fact]
    public async Task Agent_profiles_persist_model_and_prompt_and_reject_unknown_profiles()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var settings = JsonSerializer.Deserialize<DashboardSettings>("""{"Instructions":"Legacy preference","Actions":[]}""")!;
        Assert.Equal(DashboardSettings.DefaultAgentId, Assert.Single(settings.Agents).Id);
        var profile = new DashboardAgentProfile
        {
            Name = "Research", ConnectionId = fixture.ConnectionId, Model = "test", Instructions = "Cite evidence for reports"
        };
        settings.Agents.Add(profile);
        await fixture.Store.SaveSettingsAsync(fixture.ProjectId, settings);
        var model = new ScriptedModel("""{"content":"Ready to research."}""");
        var agent = new BuildChatAgent(fixture.Store, model, fixture.Service, fixture.Permissions, fixture.Tenant);
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, agent, new(), new NoJs(), fixture.Service, fixture.Permissions);
        await vm.LoadAsync(fixture.ProjectId);
        vm.SelectAgent(profile.Id.ToString());
        Assert.Equal(fixture.ConnectionId, vm.Conversation.ConnectionId);
        Assert.Equal("test", vm.Conversation.Model);
        await vm.NewAsync();
        Assert.Equal(profile.Id, vm.Conversation.AgentId);
        await vm.OpenAgentAsync();
        Assert.Equal(profile.Id, vm.EditedAgent!.Id);
        Assert.Contains("Cite evidence for reports", vm.AgentPrompt);
        var conversation = new BuildConversation { ProjectId = fixture.ProjectId, AgentId = profile.Id, ConnectionId = Guid.NewGuid(), Model = "wrong" };
        await agent.QueueAsync(conversation, "Help with research");
        Assert.Equal(fixture.ConnectionId, conversation.ConnectionId);
        Assert.Equal("test", conversation.Model);
        await agent.RunAsync(conversation, default);
        Assert.Equal(BuildRunStatus.Completed, conversation.Status);
        Assert.Contains("Cite evidence for reports", conversation.SystemPrompt);
        Assert.Contains("Legacy preference", conversation.SystemPrompt);
        Assert.Equal(profile.Id, (await fixture.Store.LoadAsync(fixture.ProjectId, conversation.Id)).AgentId);
        conversation.AgentId = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.QueueAsync(conversation, "Unknown agent"));
        settings.Agents.Add(new() { Name = " research " });
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.SaveSettingsAsync(fixture.ProjectId, settings));
        await vm.DisposePageAsync();
    }

    [Fact]
    public async Task Dashboard_settings_tabs_separate_controls_and_preserve_unsaved_edits()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections,
            fixture.Provider.GetRequiredService<BuildChatAgent>(), new(), new NoJs(), fixture.Service, fixture.Permissions);
        await using var provider = new ServiceCollection().AddLogging().AddSingleton(vm)
            .AddSingleton<IJSRuntime, NoJs>().AddTransient<RunOutputViewModel>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        async Task<string> RenderAsync(string section) => await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<PlaceContext.Host.Components.Pages.DashboardAgentSettings>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["ProjectId"] = fixture.ProjectId, ["Section"] = section }))).ToHtmlString());
        var html = await RenderAsync("agent");
        Assert.Contains("id=\"agent-model\"", html);
        Assert.DoesNotContain("id=\"agent-profile-instructions\"", html);
        foreach (var section in new[] { "agent", "instructions", "tools", "context", "agents", "dashboard" })
            Assert.Contains($"/dashboard/settings/{section}", html);
        vm.EditedAgent!.Instructions = "Unsaved instructions survive tab changes";
        vm.AgentMemory = "Unsaved memory";
        var selected = vm.AgentEditId;
        html = await RenderAsync("instructions");
        Assert.Contains("Unsaved instructions survive tab changes", html);
        Assert.DoesNotContain("id=\"agent-model\"", html);
        Assert.DoesNotContain("Connected tools (MCP)", html);
        html = await RenderAsync("tools");
        Assert.Contains("Connected tools (MCP)", html);
        Assert.DoesNotContain("id=\"agent-profile-instructions\"", html);
        html = await RenderAsync("context");
        Assert.Contains("Unsaved memory", html);
        Assert.Contains("Save memory", html);
        Assert.DoesNotContain("Save changes", html);
        await RenderAsync("agents");
        html = await RenderAsync("dashboard");
        Assert.Contains("dashboard-action-title", html);
        Assert.Contains("Customize dashboard", html);
        Assert.DoesNotContain("settings-tabs", html);
        Assert.DoesNotContain("id=\"agent-model\"", html);
        await RenderAsync("instructions");
        Assert.Equal(selected, vm.AgentEditId);
        Assert.Equal("Unsaved instructions survive tab changes", vm.EditedAgent!.Instructions);
        Assert.Equal("Unsaved memory", vm.AgentMemory);
        Assert.NotEqual(vm.EditedAgent.Instructions, (await fixture.Store.GetSettingsAsync(fixture.ProjectId)).Agents.Single(a => a.Id == selected).Instructions);
    }

    [Fact]
    public async Task Agent_settings_add_profiles_and_require_model_setup_before_chat()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var agent = fixture.Provider.GetRequiredService<BuildChatAgent>();
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, agent, new(), new NoJs(), fixture.Service, fixture.Permissions);
        await vm.LoadAsync(fixture.ProjectId);
        await vm.OpenAgentAsync();
        vm.AddAgent();
        var id = vm.EditedAgent!.Id;
        await vm.SaveAgentAsync();
        Assert.Null(vm.AgentError);
        vm.SelectAgent(id.ToString());
        Assert.False(vm.CanStartChat);
        await vm.StartWorkflowChatAsync();
        Assert.True(vm.WorkflowPickerOpen);
        await vm.OpenAgentAsync();
        vm.SelectAgentConnection(fixture.ConnectionId.ToString());
        Assert.Equal("test", vm.EditedAgent!.Model);
        await vm.SaveAgentAsync();
        Assert.True(vm.CanStartChat);
        vm.Conversation.Status = BuildRunStatus.Running;
        vm.SelectAgent(DashboardSettings.DefaultAgentId.ToString());
        Assert.Equal(id, vm.Conversation.AgentId);
        await vm.DisposePageAsync();
    }

    [Fact]
    public async Task Agent_MCP_assignments_are_saved_only_for_enabled_connections_in_this_project()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var allowed = await fixture.SeedMcpAsync();
        var disabled = await fixture.SeedMcpAsync(enabled: false);
        var other = await fixture.SeedMcpAsync(projectId: Guid.NewGuid());
        var settings = new DashboardSettings();
        settings.Agents[0].McpConnectionIds = [allowed.Id];
        await fixture.Store.SaveSettingsAsync(fixture.ProjectId, settings);
        Assert.Equal(allowed.Id, Assert.Single((await fixture.Store.GetSettingsAsync(fixture.ProjectId)).Agents[0].McpConnectionIds));
        foreach (var invalid in new[] { disabled.Id, other.Id, Guid.NewGuid() })
        {
            settings.Agents[0].McpConnectionIds = [invalid];
            await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.SaveSettingsAsync(fixture.ProjectId, settings));
        }
        settings.Agents[0].McpConnectionIds = [allowed.Id, allowed.Id];
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.SaveSettingsAsync(fixture.ProjectId, settings));
        var mcp = new AgentMcp { Connections = [allowed, disabled] };
        var agent = new BuildChatAgent(fixture.Store, new ScriptedModel(), fixture.Service, fixture.Permissions, fixture.Tenant, mcp: mcp);
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, agent, new(), new NoJs(), fixture.Service, fixture.Permissions, mcp: mcp);
        await vm.LoadAsync(fixture.ProjectId);
        await vm.OpenAgentAsync();
        Assert.Null(vm.AgentError);
        vm.ToggleAgentMcp(disabled.Id, true);
        Assert.Equal(allowed.Id, Assert.Single(vm.EditedAgent!.McpConnectionIds));
        vm.ToggleAgentMcp(allowed.Id, false);
        await vm.SaveAgentAsync();
        Assert.Empty((await fixture.Store.GetSettingsAsync(fixture.ProjectId)).Agents[0].McpConnectionIds);
        Assert.DoesNotContain("call_mcp_tool", vm.AgentTools);
        await vm.DisposePageAsync();
    }

    [Fact]
    public async Task Assigned_agent_MCP_tools_are_available_but_unassigned_disabled_and_unscoped_connections_are_blocked()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var allowed = await fixture.SeedMcpAsync();
        var unassigned = await fixture.SeedMcpAsync();
        var disabled = await fixture.SeedMcpAsync(enabled: false);
        var otherProjectId = Guid.NewGuid();
        var mcp = new AgentMcp { Connections = [allowed, unassigned, disabled] };
        var settings = new DashboardSettings();
        settings.Agents[0].McpConnectionIds = [allowed.Id];
        await fixture.Store.SaveSettingsAsync(fixture.ProjectId, settings);
        var reply = new JsonObject
        {
            ["tool_calls"] = new JsonArray(new JsonObject
            {
                ["id"] = "mcp", ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = "call_mcp_tool", ["arguments"] = JsonSerializer.Serialize(new { connectionId = allowed.Id, toolName = "lookup", arguments = new { } }) }
            })
        };
        var model = new ScriptedModel(reply.ToJsonString());
        var agent = new BuildChatAgent(fixture.Store, model, fixture.Service, fixture.Permissions, fixture.Tenant, mcp: mcp);
        var inspection = await agent.InspectAsync(fixture.ProjectId);
        Assert.Contains("call_mcp_tool", inspection.Tools);
        Assert.Contains(allowed.Id.ToString(), inspection.Prompt);
        Assert.DoesNotContain(unassigned.Id.ToString(), inspection.Prompt);
        Assert.DoesNotContain("private-token", inspection.Prompt);
        var conversation = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test" };
        await agent.QueueAsync(conversation, "Look up a report with the assigned connection");
        await agent.RunAsync(conversation, default);
        Assert.Equal(BuildRunStatus.Completed, conversation.Status);
        Assert.Equal(1, mcp.Calls);
        Assert.Equal(fixture.ProjectId, mcp.ProjectId);
        var tools = new BuildTools(fixture.ProjectId, fixture.Service, fixture.Permissions, fixture.Store, mcp: mcp,
            mcpConnectionIds: new HashSet<Guid> { allowed.Id, disabled.Id, otherProjectId });
        var arguments = JsonSerializer.SerializeToElement(new { });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.list_mcp_tools(unassigned.Id, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.call_mcp_tool(unassigned.Id, "lookup", arguments, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => tools.call_mcp_tool(disabled.Id, "lookup", arguments, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => tools.call_mcp_tool(otherProjectId, "lookup", arguments, default));
        fixture.Permissions.Allowed = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.call_mcp_tool(allowed.Id, "lookup", arguments, default));
        Assert.Equal(1, mcp.Calls);
    }

    private sealed class AgentMcp : IMcpClientService
    {
        public IReadOnlyList<McpConnectionInfo> Connections = [];
        public int Calls;
        public Guid ProjectId;
        public Task<IReadOnlyList<McpConnectionInfo>> ListConnectionsAsync(Guid projectId, CancellationToken ct = default)
        {
            ProjectId = projectId;
            return Task.FromResult(Connections);
        }
        public Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(Guid connectionId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<McpToolDefinition>>([new("lookup", "Look up a report", JsonSerializer.SerializeToElement(new { type = "object" }))]);
        public Task<McpToolResult> CallToolAsync(Guid connectionId, string toolName, JsonElement? arguments = null, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new McpToolResult(true, "Report found", null));
        }
    }

    [Fact]
    public async Task Dashboard_settings_are_encrypted_private_and_validated_without_overwriting_memory()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        await fixture.Store.SaveMemoryAsync(fixture.ProjectId, "Remember this");
        await fixture.Store.SaveSettingsAsync(fixture.ProjectId, new() { Instructions = "Use brief answers", Actions = [new("Inspect", "Show statistics")] });
        Assert.Equal("Remember this", await fixture.Store.GetMemoryAsync(fixture.ProjectId));
        Assert.Equal("Use brief answers", (await fixture.Store.GetSettingsAsync(fixture.ProjectId)).Instructions);
        using var scope = fixture.Provider.CreateScope();
        Assert.DoesNotContain("Use brief answers", (await scope.ServiceProvider.GetRequiredService<AppDbContext>().BuildMemories.SingleAsync()).Settings);
        fixture.User.UserId = Guid.NewGuid();
        Assert.Empty((await fixture.Store.GetSettingsAsync(fixture.ProjectId)).Instructions);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.SaveSettingsAsync(fixture.ProjectId, new() { Instructions = new string('x', 8001) }));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.SaveSettingsAsync(fixture.ProjectId, new() { Actions = [new("", "x")] }));
        fixture.Permissions.Allowed = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Store.GetSettingsAsync(fixture.ProjectId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Store.SaveSettingsAsync(fixture.ProjectId, new()));
    }

    [Fact]
    public async Task Each_private_and_shared_agent_has_its_own_persisted_dashboard()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        var settings = new DashboardSettings();
        var privateAgent = new DashboardAgentProfile { Name = "Private analyst" };
        settings.Agents.Add(privateAgent);
        settings.TileSizes["chat"] = "wide";
        await f.Store.SaveSettingsAsync(f.ProjectId, settings);
        var shared = new DbProjectAgentStore(f.Provider.GetRequiredService<IServiceScopeFactory>(),
            f.User, f.Tenant, f.Permissions, f.Provider.GetRequiredService<IDataEncryptor>(), f.Connections);
        var sharedAgent = new DashboardAgentProfile { Name = "Shared analyst" };
        await shared.SaveAsync(f.ProjectId, new() { Agents = [sharedAgent] });
        var vm = new BuildChatViewModel(f.Store, f.Connections,
            f.Provider.GetRequiredService<BuildChatAgent>(), new(), new NoJs(), f.Service, f.Permissions, projectAgents: shared);
        await vm.LoadAsync(f.ProjectId);
        Assert.Equal(3, vm.DashboardAgents.Count());
        Assert.Equal("wide", vm.Tiles.TileSize("chat"));
        vm.SelectAgent(privateAgent.Id.ToString());
        Assert.Equal("square", vm.Tiles.TileSize("chat"));
        Assert.True(await vm.MoveDashboardTileAsync("chat", 0, false, 2, 2));
        Assert.True(await vm.ResizeDashboardTileAsync("chat", "large"));
        await vm.RemoveDashboardTileAsync("workflow");
        vm.SelectAgent(sharedAgent.Id.ToString());
        Assert.Contains("workflow", vm.Tiles.OrderedTiles());
        Assert.Equal("square", vm.Tiles.TileSize("chat"));
        var chart = new BuildChatViewModel.ChartPreview(ChartSpec.TryParse("""{"type":"bar","title":"Runs","labels":["Succeeded"],"series":[{"name":"Runs","values":[12]}]}""")!, "Recent runs", Live: true);
        await vm.PinDashboardChartAsync(chart);
        Assert.Single(vm.Tiles.Charts);
        vm.SelectAgent(privateAgent.Id.ToString());
        Assert.Empty(vm.Tiles.Charts);
        Assert.Equal("chat", vm.Tiles.OrderedTiles()[0]);
        Assert.Equal(new DashboardTilePosition(2, 2), vm.Tiles.TilePositions["chat"]);
        Assert.Equal("large", vm.Tiles.TileSize("chat"));
        Assert.DoesNotContain("workflow", vm.Tiles.OrderedTiles());
        vm.SelectAgent(DashboardSettings.DefaultAgentId.ToString());
        Assert.Equal("wide", vm.Tiles.TileSize("chat"));
        Assert.Contains("workflow", vm.Tiles.OrderedTiles());
        Assert.Empty(vm.Tiles.Charts);
        var reloaded = await f.Store.GetSettingsAsync(f.ProjectId);
        Assert.Equal("large", reloaded.ForAgent(privateAgent.Id).TileSize("chat"));
        Assert.Single(reloaded.ForAgent(sharedAgent.Id).Charts);
        Assert.Equal("wide", reloaded.TileSize("chat"));
        reloaded.ForAgent(privateAgent.Id).TileSizes["chat"] = "invalid";
        await Assert.ThrowsAsync<ArgumentException>(() => f.Store.SaveSettingsAsync(f.ProjectId, reloaded));
        await vm.DisposePageAsync();
    }

    [Theory]
    [InlineData("chat")]
    [InlineData("chart")]
    [InlineData("workflow")]
    [InlineData("custom")]
    public async Task Dashboard_tile_editor_cancels_validates_and_saves_without_sending_a_message(string key)
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections,
            fixture.Provider.GetRequiredService<BuildChatAgent>(), new(), new NoJs(), fixture.Service, fixture.Permissions);
        await vm.LoadAsync(fixture.ProjectId);
        if (key == "custom") key = vm.Tiles.Actions[0].Id.ToString();
        var original = vm.Tiles.TileAction(key)!;
        var order = vm.Tiles.OrderedTiles().ToArray();
        vm.EditDashboardTile(key);
        vm.TileTitleDraft = "Cancelled edit";
        vm.CloseTileEditor();
        Assert.Equal(original, vm.Tiles.TileAction(key));
        vm.EditDashboardTile(key);
        vm.TileTitleDraft = "";
        await vm.SaveDashboardTileAsync();
        Assert.NotNull(vm.TileEditError);
        Assert.Equal(key, vm.EditingTileKey);
        Assert.Equal(original, vm.Tiles.TileAction(key));
        vm.TileTitleDraft = "My tile";
        vm.TileDescriptionDraft = "A short description";
        if (key != "workflow") vm.TilePromptDraft = "Show project activity without making changes.";
        await vm.SaveDashboardTileAsync();
        Assert.Null(vm.EditingTileKey);
        var saved = await fixture.Store.GetSettingsAsync(fixture.ProjectId);
        Assert.Equal("My tile", saved.TileAction(key)!.Title);
        Assert.Equal("A short description", saved.TileAction(key)!.Description);
        Assert.Equal(key == "workflow" ? original.Prompt : vm.TilePromptDraft, saved.TileAction(key)!.Prompt);
        Assert.Equal(order, saved.OrderedTiles());
        Assert.Empty(vm.Conversation.Messages);
    }

    [Fact]
    public async Task Dashboard_edits_order_and_pinned_charts_survive_reload_without_losing_settings()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var legacy = JsonSerializer.Deserialize<DashboardSettings>("""{"Instructions":"Keep this","Actions":[{"Title":"Inspect","Prompt":"Show statistics"}]}""")!;
        await fixture.Store.SaveSettingsAsync(fixture.ProjectId, legacy);
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections,
            fixture.Provider.GetRequiredService<BuildChatAgent>(), new(), new NoJs(), fixture.Service, fixture.Permissions);
        await vm.LoadAsync(fixture.ProjectId);
        var actionId = Assert.Single(vm.Dashboard.Actions).Id;
        Assert.Equal("square", vm.Dashboard.TileSize(actionId.ToString()));
        await vm.ResizeDashboardTileAsync(actionId.ToString(), "wide");
        Assert.Equal("wide", (await fixture.Store.GetSettingsAsync(fixture.ProjectId)).TileSize(actionId.ToString()));
        Assert.True(await vm.MoveDashboardTileAsync(actionId.ToString(), 0));
        Assert.Equal(actionId.ToString(), (await fixture.Store.GetSettingsAsync(fixture.ProjectId)).OrderedTiles()[0]);
        var chart = new BuildChatViewModel.ChartPreview(ChartSpec.TryParse("""{"type":"bar","title":"Runs","labels":["Succeeded"],"series":[{"name":"Runs","values":[12]}]}""")!, "Project runs in the last 24 hours", Live: true);
        await vm.PinDashboardChartAsync(chart);
        await vm.PinDashboardChartAsync(chart);
        Assert.True(vm.IsChartPinned(chart));
        var pinned = Assert.Single(vm.Dashboard.Charts);
        await vm.MoveDashboardTileAsync(pinned.Id.ToString(), 1);
        await vm.OpenAgentAsync();
        vm.EditDashboardAction(actionId, "Run report", "Report recent run results without making changes.");
        vm.NewActionTitle = "Artifacts";
        vm.NewActionPrompt = "List recent artifacts.";
        vm.AddDashboardAction();
        await vm.SaveAgentAsync();
        Assert.Null(vm.AgentError);
        var saved = await fixture.Store.GetSettingsAsync(fixture.ProjectId);
        Assert.Equal("Keep this", saved.Instructions);
        Assert.Equal("Run report", saved.Actions[0].Title);
        Assert.Equal("Report recent run results without making changes.", saved.Actions[0].Prompt);
        Assert.Equal(pinned, Assert.Single(saved.Charts));
        Assert.Equal("wide", saved.TileSize(actionId.ToString()));
        Assert.Equal(new[] { actionId.ToString(), pinned.Id.ToString(), "workflow", "chat", "chart", saved.Actions[1].Id.ToString() }, saved.OrderedTiles());
        vm.RemoveDashboardAction(vm.AgentDraft.Actions[0]);
        vm.RemoveDashboardChart(Assert.Single(vm.AgentDraft.Charts));
        await vm.SaveAgentAsync();
        Assert.Null(vm.AgentError);
        saved = await fixture.Store.GetSettingsAsync(fixture.ProjectId);
        Assert.Empty(saved.Charts);
        Assert.DoesNotContain(actionId.ToString(), saved.TileOrder);
        Assert.DoesNotContain(pinned.Id.ToString(), saved.TileOrder);
        Assert.Empty(saved.TileSizes);
        await vm.DisposePageAsync();
    }

    [Fact]
    public async Task Dashboard_tiles_can_be_removed_and_restored_without_losing_configuration()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections,
            fixture.Provider.GetRequiredService<BuildChatAgent>(), new(), new NoJs(), fixture.Service, fixture.Permissions);
        await vm.LoadAsync(fixture.ProjectId);
        var keys = vm.Dashboard.OrderedTiles().ToArray();
        foreach (var key in keys) await vm.RemoveDashboardTileAsync(key);
        Assert.Empty(vm.Dashboard.OrderedTiles());
        var saved = await fixture.Store.GetSettingsAsync(fixture.ProjectId);
        Assert.Equal(keys.Length, saved.HiddenTiles.Count);
        Assert.Equal(2, saved.Actions.Count);
        await vm.OpenAgentAsync();
        foreach (var key in keys) vm.RestoreDashboardTile(key);
        await vm.SaveAgentAsync();
        Assert.Null(vm.AgentError);
        saved = await fixture.Store.GetSettingsAsync(fixture.ProjectId);
        Assert.Empty(saved.HiddenTiles);
        Assert.Equal(keys, saved.OrderedTiles());
        await vm.DisposePageAsync();
    }

    [Fact]
    public async Task Dashboard_tiles_can_move_to_a_new_row_and_back()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, null!, new(), new NoJs(), fixture.Service, fixture.Permissions);
        await vm.LoadAsync(fixture.ProjectId);
        await vm.MoveDashboardTileAsync("workflow", vm.Dashboard.OrderedTiles().Count - 1, newRow: true);
        var saved = await fixture.Store.GetSettingsAsync(fixture.ProjectId);
        Assert.Equal("workflow", saved.OrderedTiles().Last());
        Assert.Contains("workflow", saved.TileRowStarts);
        Assert.True(await vm.ResizeDashboardTileAsync("workflow", "large"));
        Assert.Contains("workflow", vm.Dashboard.TileRowStarts);
        await vm.MoveDashboardTileAsync("workflow", 0);
        saved = await fixture.Store.GetSettingsAsync(fixture.ProjectId);
        Assert.Equal("workflow", saved.OrderedTiles().First());
        Assert.DoesNotContain("workflow", saved.TileRowStarts);
        await vm.DisposePageAsync();
    }

    [Fact]
    public async Task Dashboard_reorder_keeps_the_saved_layout_when_persistence_fails()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, null!, new(), new NoJs(), fixture.Service, fixture.Permissions);
        await vm.LoadAsync(fixture.ProjectId);
        var order = vm.Dashboard.OrderedTiles().ToArray();
        fixture.Permissions.Allowed = false;
        Assert.False(await vm.MoveDashboardTileAsync("chat", 0));
        Assert.NotNull(vm.Error);
        Assert.Equal(order, vm.Dashboard.OrderedTiles());
        Assert.False(await vm.ResizeDashboardTileAsync("chat", "large"));
        Assert.Equal("square", vm.Dashboard.TileSize("chat"));
        await vm.RemoveDashboardTileAsync("chat");
        Assert.NotNull(vm.Error);
        Assert.Equal(order, vm.Dashboard.OrderedTiles());
        await vm.DisposePageAsync();
    }

    [Theory]
    [InlineData("{\"TileOrder\":[\"unknown\"]}")]
    [InlineData("{\"TileOrder\":[\"chat\",\"chat\"]}")]
    [InlineData("{\"Charts\":[{\"ChartJson\":\"{}\",\"Source\":\"Invalid chart\"}]}")]
    [InlineData("{\"Charts\":null}")]
    [InlineData("{\"HiddenTiles\":null}")]
    [InlineData("{\"HiddenTiles\":[\"unknown\"]}")]
    [InlineData("{\"HiddenTiles\":[\"chat\",\"chat\"]}")]
    [InlineData("{\"TileRowStarts\":null}")]
    [InlineData("{\"TileRowStarts\":[\"unknown\"]}")]
    [InlineData("{\"TileRowStarts\":[\"chat\",\"chat\"]}")]
    [InlineData("{\"TileSizes\":null}")]
    [InlineData("{\"TileSizes\":{\"unknown\":\"wide\"}}")]
    [InlineData("{\"TileSizes\":{\"chat\":\"huge\"}}")]
    public void Dashboard_rejects_invalid_layouts_and_chart_data(string json)
    {
        var settings = JsonSerializer.Deserialize<DashboardSettings>(json)!;
        Assert.Throws<ArgumentException>(settings.Validate);
    }

    [Fact]
    public async Task Live_dashboard_charts_query_again_clear_stale_values_and_recover_without_chat()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var dispatcher = new ToolDispatcher(fixture.ProjectId);
        var service = new PlaceContextService(dispatcher, null!, null!, null!);
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, null!, new(), new NoJs(), service, fixture.Permissions);
        await vm.LoadAsync(fixture.ProjectId);
        var tools = new BuildTools(fixture.ProjectId, service, fixture.Permissions, fixture.Store);
        const string sql = "SELECT status, count(*) AS total FROM orders GROUP BY status";
        var output = JsonSerializer.SerializeToNode(await tools.show_live_chart("Orders", sql: sql))!;
        vm.Conversation.Messages.Add(new JsonObject
        {
            ["role"] = "assistant", ["tool_calls"] = new JsonArray(new JsonObject
            { ["id"] = "live-chart", ["function"] = new JsonObject { ["name"] = "show_live_chart" } })
        });
        var preview = vm.ResultChart(new JsonObject { ["role"] = "tool", ["tool_call_id"] = "live-chart", ["content"] = output.ToJsonString() })!;
        Assert.True(preview.Live);
        Assert.Equal(sql, preview.Sql);
        await vm.NewAsync();
        await vm.PinDashboardChartAsync(preview with { Live = false });
        Assert.Empty(vm.Dashboard.Charts);
        await vm.PinDashboardChartAsync(preview);
        var pinned = Assert.Single(vm.Dashboard.Charts);
        Assert.Equal(2, vm.DashboardChartResults[pinned.Id].Spec!.Series[0].Values[0]);
        vm.OpenDashboardChart(pinned);
        dispatcher.Data = dispatcher.Data with { Rows = [["Ready", "9"]] };
        await vm.RefreshDashboardChartsAsync();
        Assert.Equal(9, vm.DashboardChartResults[pinned.Id].Spec!.Series[0].Values[0]);
        Assert.Equal(9, vm.PreviewChart!.Spec.Series[0].Values[0]);
        Assert.Equal(fixture.ProjectId, dispatcher.DataProject);
        Assert.Equal(sql, dispatcher.DataSql);
        Assert.False(vm.ChatOpen);
        Assert.Empty(vm.Conversation.Messages);
        Assert.Equal(0, dispatcher.Commands);
        fixture.Permissions.Denied = Permission.DataRead;
        var queries = dispatcher.DataQueries;
        await vm.RefreshDashboardChartsAsync();
        Assert.Equal(queries, dispatcher.DataQueries);
        Assert.Null(vm.DashboardChartResults[pinned.Id].Spec);
        Assert.NotNull(vm.DashboardChartResults[pinned.Id].Error);
        fixture.Permissions.Denied = null;
        dispatcher.Data = dispatcher.Data with { Rows = [] };
        await vm.RefreshDashboardChartsAsync();
        Assert.Null(vm.DashboardChartResults[pinned.Id].Spec);
        Assert.Null(vm.DashboardChartResults[pinned.Id].Error);
        dispatcher.Data = dispatcher.Data with { Rows = [["Ready", "13"]] };
        await vm.RefreshDashboardChartsAsync();
        Assert.Equal(13, vm.DashboardChartResults[pinned.Id].Spec!.Series[0].Values[0]);
        await vm.PinDashboardChartAsync(vm.PreviewChart!);
        Assert.Single(vm.Dashboard.Charts);
        vm.CloseWorkspace();
        Assert.Null(vm.ExpandedDashboardChartId);
        dispatcher.PendingData = new();
        var pending = vm.RefreshDashboardChartsAsync();
        await vm.DisposePageAsync();
        dispatcher.PendingData.SetResult(dispatcher.Data);
        await pending;
        Assert.Empty(vm.DashboardChartResults);
    }

    [Fact]
    public async Task Live_activity_charts_use_current_counts_and_check_permissions_each_time()
    {
        var projectId = Guid.NewGuid();
        var dispatcher = new ToolDispatcher(projectId);
        var permissions = new Permissions();
        var tools = new BuildTools(projectId, new PlaceContextService(dispatcher, null!, null!, null!), permissions, null!);
        var first = JsonSerializer.SerializeToNode(await tools.show_live_chart("Runs"))!;
        Assert.Equal(3, first["chart"]!["series"]![0]!["values"]![0]!.GetValue<int>());
        dispatcher.Counts["Running"] = 8;
        var second = JsonSerializer.SerializeToNode(await tools.show_live_chart("Runs"))!;
        Assert.Equal(8, second["chart"]!["series"]![0]!["values"]![0]!.GetValue<int>());
        permissions.Denied = Permission.JobsView;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.show_live_chart("Runs"));
        permissions.Denied = Permission.DataRead;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.show_live_chart("Orders", sql: "SELECT status, count(*) FROM orders GROUP BY status"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.list_project_tables(default));
        Assert.Equal(0, dispatcher.DataQueries);
        await Assert.ThrowsAsync<ArgumentException>(() => tools.show_live_chart("Runs", type: "script"));
        Assert.Equal(0, dispatcher.Commands);
    }

    [Fact]
    public async Task Chart_query_edits_preserve_layout_and_keep_the_previous_query_on_failure()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var dispatcher = new ToolDispatcher(fixture.ProjectId);
        var service = new PlaceContextService(dispatcher, null!, null!, null!);
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, null!, new(), new NoJs(), service, fixture.Permissions);
        await vm.LoadAsync(fixture.ProjectId);
        var spec = new ChartSpec("bar", "Orders", ["Ready"], [new("Total", [2])]);
        await vm.PinDashboardChartAsync(new(spec, "Project data", true, "SELECT status, total FROM orders"));
        var chart = Assert.Single(vm.Dashboard.Charts);
        await vm.ResizeDashboardTileAsync(chart.Id.ToString(), "tall");
        await vm.MoveDashboardTileAsync(chart.Id.ToString(), 0);
        await vm.EditChartQueryAsync(chart);
        Assert.Null(vm.ChartQueryError);
        Assert.Equal(chart.Sql, vm.ChartSqlDraft);
        await vm.SaveChartQueryAsync("");
        Assert.NotNull(vm.ChartQueryError);
        Assert.Equal(chart, Assert.Single(vm.Dashboard.Charts));
        dispatcher.DataError = "SQL is invalid";
        await vm.SaveChartQueryAsync("broken SQL");
        Assert.Equal("SQL is invalid", vm.ChartQueryError);
        Assert.Equal(chart.Sql, Assert.Single((await fixture.Store.GetSettingsAsync(fixture.ProjectId)).Charts).Sql);
        dispatcher.DataError = null;
        dispatcher.Data = dispatcher.Data with { Rows = [["Ready", "27"]] };
        const string updatedSql = "SELECT status, sum(total) AS total FROM orders GROUP BY status";
        await vm.SaveChartQueryAsync(updatedSql);
        Assert.Null(vm.ChartQueryError);
        Assert.Null(vm.EditingChart);
        var saved = await fixture.Store.GetSettingsAsync(fixture.ProjectId);
        Assert.Equal(updatedSql, Assert.Single(saved.Charts).Sql);
        Assert.Equal("tall", saved.TileSize(chart.Id.ToString()));
        Assert.Equal(chart.Id.ToString(), saved.OrderedTiles()[0]);
        Assert.Equal(27, vm.DashboardChartResults[chart.Id].Spec!.Series[0].Values[0]);
        await vm.EditChartQueryAsync(Assert.Single(vm.Dashboard.Charts));
        Assert.Equal(updatedSql, vm.ChartSqlDraft);
        fixture.Permissions.Denied = Permission.DataRead;
        await vm.SaveChartQueryAsync("SELECT status, total FROM other_orders");
        Assert.NotNull(vm.ChartQueryError);
        Assert.Equal(updatedSql, Assert.Single(vm.Dashboard.Charts).Sql);
        await vm.DisposePageAsync();
    }

    [Fact]
    public async Task Agent_panel_uses_execution_prompt_and_saves_tiles_without_removing_system_action()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var agent = fixture.Provider.GetRequiredService<BuildChatAgent>();
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, agent, new(), new NoJs(), fixture.Service, fixture.Permissions);
        await vm.LoadAsync(fixture.ProjectId);
        await vm.OpenAgentAsync();
        Assert.Null(vm.AgentError);
        Assert.True(vm.AgentOpen);
        Assert.False(vm.WorkspaceOpen);
        Assert.False(vm.ChatOpen);
        Assert.Contains(fixture.ProjectId.ToString(), vm.AgentPrompt);
        Assert.Contains("run_workflow", vm.AgentTools);
        vm.AgentDraft.Instructions = "Prefer concise summaries";
        vm.AgentDraft.Actions.Clear();
        await vm.SaveAgentAsync();
        Assert.Contains("Prefer concise summaries", vm.AgentPrompt);
        Assert.Empty(vm.Dashboard.Actions);
        await vm.StartWorkflowChatAsync();
        Assert.False(vm.AgentOpen);
        Assert.True(vm.WorkflowPickerOpen);
        await vm.OpenAgentAsync();
        Assert.True(vm.WorkflowPickerOpen);
        vm.CloseAgent();
        Assert.True(vm.WorkspaceOpen);
        Assert.True(vm.WorkflowPickerOpen);
        Assert.Empty(vm.Conversation.Messages);
        vm.Draft = "Explain how to run an existing workflow";
        await vm.SendAsync();
        Assert.Equal(BuildRunStatus.Queued, vm.Conversation.Status);
        Assert.Contains("run an existing workflow", BuildChatViewModel.Text(vm.Conversation.Messages[0]!));
        await agent.RunAsync(vm.Conversation, default);
        Assert.Contains("Prefer concise summaries", vm.Conversation.SystemPrompt);
        Assert.Contains("run_workflow", vm.Conversation.ToolSchema);
        var saved = await fixture.Store.LoadAsync(fixture.ProjectId, vm.Conversation.Id);
        Assert.Equal(vm.Conversation.SystemPrompt, saved.SystemPrompt);
        await vm.DisposePageAsync();
    }

    [Fact]
    public async Task Workflow_tile_and_selection_are_immediate_without_models_and_preserve_drafts()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var workflow = new JobChainView(Guid.NewGuid(), fixture.ProjectId, "Report", "Build a report", [], DateTimeOffset.UtcNow);
        var dispatcher = new ToolDispatcher(fixture.ProjectId) { Workflow = workflow };
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, null!, new(), new NoJs(), new(dispatcher, null!, null!, null!), fixture.Permissions);
        await vm.LoadAsync(fixture.ProjectId);
        Assert.False(vm.ChatOpen);
        vm.Conversation.ConnectionId = Guid.Empty;
        vm.Draft = "Keep my draft";
        await vm.StartWorkflowChatAsync();
        Assert.True(vm.ChatOpen);
        Assert.True(vm.WorkflowPickerOpen);
        Assert.False(vm.WorkflowPickerLoading);
        Assert.Equal(workflow, Assert.Single(vm.AvailableWorkflows));
        Assert.Empty(vm.Conversation.Messages);
        await vm.SelectWorkflowAsync(Guid.NewGuid());
        Assert.Null(vm.PreviewWorkflow);
        await vm.SelectWorkflowAsync(workflow.Id);
        Assert.Equal(workflow, vm.PreviewWorkflow);
        Assert.True(vm.WorkflowSelectedForRun);
        Assert.False(vm.WorkflowPickerOpen);
        Assert.Equal("Keep my draft", vm.Draft);
        await vm.PrepareWorkflowRunAsync();
        Assert.Empty(vm.Conversation.Messages);
        Assert.Equal(0, dispatcher.Commands);
        await vm.DisposePageAsync();
    }

    [Theory]
    [InlineData(Permission.ProjectsView)]
    [InlineData(Permission.ChainsManage)]
    [InlineData(Permission.JobsRun)]
    public async Task Workflow_picker_checks_permissions_and_filters_project(string permission)
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var dispatcher = new ToolDispatcher(fixture.ProjectId)
        {
            Workflow = new(Guid.NewGuid(), Guid.NewGuid(), "Other project", null, [], DateTimeOffset.UtcNow)
        };
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, null!, new(), new NoJs(), new(dispatcher, null!, null!, null!), fixture.Permissions);
        await vm.LoadAsync(fixture.ProjectId);
        await vm.StartWorkflowChatAsync();
        Assert.Empty(vm.AvailableWorkflows);
        Assert.Null(vm.WorkflowPickerError);
        fixture.Permissions.Denied = permission;
        await vm.StartWorkflowChatAsync();
        Assert.NotNull(vm.WorkflowPickerError);
        Assert.Empty(vm.AvailableWorkflows);
        Assert.Equal(0, dispatcher.Commands);
        await vm.DisposePageAsync();
    }

    [Theory]
    [InlineData("none", false)]
    [InlineData("read", false)]
    [InlineData("run", true)]
    [InlineData("manage", true)]
    public async Task Workflow_picker_only_lists_workflows_the_selected_agent_can_run(string access, bool canRun)
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var workflow = new JobChainView(Guid.NewGuid(), fixture.ProjectId, "Report", null, [], DateTimeOffset.UtcNow);
        var dispatcher = new ToolDispatcher(fixture.ProjectId) { Workflow = workflow };
        var settings = await fixture.Store.GetSettingsAsync(fixture.ProjectId);
        settings.Agents[0].WorkflowAccess = access;
        await fixture.Store.SaveSettingsAsync(fixture.ProjectId, settings);
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, null!, new(), new NoJs(), new(dispatcher, null!, null!, null!), fixture.Permissions);
        await vm.LoadAsync(fixture.ProjectId);
        await vm.StartWorkflowChatAsync();
        Assert.True(vm.WorkflowPickerOpen);
        Assert.Equal(canRun, !vm.WorkflowPickerRestricted);
        Assert.Equal(canRun ? 1 : 0, vm.AvailableWorkflows.Count);
        await vm.DisposePageAsync();
    }

    [Fact]
    public async Task Workflow_picker_uses_the_selected_dashboard_agents_run_access()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var workflow = new JobChainView(Guid.NewGuid(), fixture.ProjectId, "Report", null, [], DateTimeOffset.UtcNow);
        var dispatcher = new ToolDispatcher(fixture.ProjectId) { Workflow = workflow };
        var restricted = new DashboardAgentProfile { Name = "Restricted", WorkflowAccess = "none" };
        var settings = await fixture.Store.GetSettingsAsync(fixture.ProjectId);
        settings.Agents.Add(restricted);
        await fixture.Store.SaveSettingsAsync(fixture.ProjectId, settings);
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, null!, new(), new NoJs(), new(dispatcher, null!, null!, null!), fixture.Permissions);
        await vm.LoadAsync(fixture.ProjectId);
        vm.SelectAgent(restricted.Id.ToString());
        await vm.StartWorkflowChatAsync();
        Assert.True(vm.WorkflowPickerRestricted);
        Assert.Equal("Restricted", vm.WorkflowPickerAgentName);
        Assert.Empty(vm.AvailableWorkflows);
        await vm.DisposePageAsync();
    }

    [Fact]
    public async Task Closed_workflow_picker_ignores_late_results()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var dispatcher = new ToolDispatcher(fixture.ProjectId) { PendingWorkflows = new() };
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, null!, new(), new NoJs(), new(dispatcher, null!, null!, null!), fixture.Permissions);
        await vm.LoadAsync(fixture.ProjectId);
        var loading = vm.StartWorkflowChatAsync();
        Assert.True(vm.WorkflowPickerOpen);
        Assert.True(vm.WorkflowPickerLoading);
        vm.CloseWorkspace();
        dispatcher.PendingWorkflows.SetResult([new(Guid.NewGuid(), fixture.ProjectId, "Late", null, [], DateTimeOffset.UtcNow)]);
        await loading;
        Assert.False(vm.WorkflowPickerOpen);
        Assert.Empty(vm.AvailableWorkflows);
        await vm.DisposePageAsync();
    }

    [Fact]
    public async Task Preparing_selected_workflow_queues_chat_with_its_real_id_without_starting_a_run()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var dispatcher = new ToolDispatcher(fixture.ProjectId)
        {
            Workflow = new(Guid.NewGuid(), fixture.ProjectId, "Report", null, [], DateTimeOffset.UtcNow)
        };
        var agent = fixture.Provider.GetRequiredService<BuildChatAgent>();
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, agent, new(), new NoJs(), new(dispatcher, null!, null!, null!), fixture.Permissions);
        await vm.LoadAsync(fixture.ProjectId);
        await vm.StartWorkflowChatAsync();
        await vm.SelectWorkflowAsync(dispatcher.Workflow.Id);
        Assert.False(vm.ChatOpen);
        await vm.PrepareWorkflowRunAsync();
        Assert.Equal(BuildRunStatus.Queued, vm.Conversation.Status);
        Assert.True(vm.PreparingWorkflowRun);
        var prompt = BuildChatViewModel.Text(Assert.Single(vm.Conversation.Messages)!);
        Assert.Contains(dispatcher.Workflow.Id.ToString(), prompt);
        Assert.Contains("required inputs", prompt);
        Assert.Equal(0, dispatcher.Commands);
        await vm.DisposePageAsync();
    }

    [Theory]
    [InlineData(0.95, true)]
    [InlineData(0.85, true)]
    [InlineData(0.84, false)]
    [InlineData(-0.1, false)]
    [InlineData(1.1, false)]
    public async Task Jev_routes_confident_navigation_without_calling_the_chat_model(double confidence, bool routed)
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var model = new ScriptedModel("""{"content":"How can I help?"}""");
        var reviewer = new Reviewer { Confidence = confidence };
        var agent = new BuildChatAgent(fixture.Store, model, fixture.Service, fixture.Permissions, fixture.Tenant, reviewer);
        var conversation = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test" };
        await agent.QueueAsync(conversation, "Click the run workflow tile");
        await agent.RunAsync(conversation, default);
        Assert.Equal(BuildRunStatus.Completed, conversation.Status);
        Assert.Equal(routed ? 0 : 1, model.Requests);
        Assert.Equal(1, reviewer.Calls);
        Assert.Equal(fixture.ProjectId, reviewer.ProjectId);
        Assert.Equal(BuildChatAgent.WorkflowPickerQuestion, reviewer.Question);
        Assert.DoesNotContain("ProjectId", reviewer.State);
        var saved = await fixture.Store.LoadAsync(fixture.ProjectId, conversation.Id);
        Assert.Equal(routed ? "choose_workflow" : null, saved.Messages.Last()?["dashboard_action"]?.ToString());
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, agent, new(), new NoJs(), fixture.Service, fixture.Permissions);
        await vm.LoadAsync(fixture.ProjectId, conversation.Id);
        Assert.Equal(routed, vm.WorkflowPickerOpen);
        await vm.DisposePageAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Jev_failure_or_timeout_falls_back_to_chat(bool timeout)
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var model = new ScriptedModel("""{"content":"Choose a workflow."}""");
        var reviewer = new Reviewer { Fail = !timeout, WaitForCancellation = timeout };
        var agent = new BuildChatAgent(fixture.Store, model, fixture.Service, fixture.Permissions, fixture.Tenant, reviewer);
        var conversation = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test" };
        await agent.QueueAsync(conversation, "Run a workflow");
        await agent.RunAsync(conversation, default).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(BuildRunStatus.Completed, conversation.Status);
        Assert.Equal(1, model.Requests);
        Assert.Null(conversation.Messages.Last()?["dashboard_action"]);
    }

    [Fact]
    public async Task Jev_skips_followups_and_cannot_be_impersonated_by_the_chat_model()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var reviewer = new Reviewer { Confidence = .99 };
        var model = new ScriptedModel("""{"content":"Here is more information.","dashboard_action":"choose_workflow"}""");
        var agent = new BuildChatAgent(fixture.Store, model, fixture.Service, fixture.Permissions, fixture.Tenant, reviewer);
        var conversation = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test" };
        await agent.QueueAsync(conversation, "Run a workflow");
        await agent.RunAsync(conversation, default);
        await agent.QueueAsync(conversation, "What does the report workflow do?");
        await agent.RunAsync(conversation, default);
        Assert.Equal(1, reviewer.Calls);
        Assert.Equal(1, model.Requests);
        Assert.Null(conversation.Messages.Last()?["dashboard_action"]);
        Assert.Equal(BuildRunStatus.Completed, conversation.Status);
    }

    [Fact]
    public async Task Dashboard_run_routes_survive_reload_and_close_without_query_strings()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var run = new ChainRunView(Guid.NewGuid(), Guid.NewGuid(), "Example", "Succeeded", [], "{}", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow) { ProjectId = fixture.ProjectId };
        var dispatcher = new ToolDispatcher(fixture.ProjectId) { Run = run };
        var nav = new DashboardNavigation(PageRoutes.DashboardRun(fixture.ProjectId, run.Id));
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, null!, new(), new NoJs(), new(dispatcher, null!, null!, null!), fixture.Permissions, navigation: nav);
        await vm.LoadAsync(fixture.ProjectId, runId: run.Id);
        Assert.Equal(run, vm.PreviewRun);
        Assert.Empty(nav.Visits);
        await vm.LoadAsync(fixture.ProjectId, runId: run.Id);
        Assert.Equal(run, vm.PreviewRun);
        vm.CloseWorkspace();
        Assert.Equal($"/project/{fixture.ProjectId}/dashboard", Assert.Single(nav.Visits));
        await vm.LoadAsync(fixture.ProjectId);
        Assert.Null(vm.PreviewRun);
        await vm.DisposePageAsync();
    }

    [Fact]
    public void Sub_agents_include_nested_branches_but_not_external_or_human_steps()
    {
        var automatic = new LoopChainActionView("agentTask", "Inspect output", "result", AgentProvider: "automatic");
        var stages = new[] { new JobChainStageView([], ElseBranch: [new([], Action: automatic)], Action: new LoopChainActionView("humanInput", "Approve", "approval")) };
        Assert.Equal(automatic, Assert.Single(BuildChatViewModel.SubAgents(stages)));
    }

    private sealed class DashboardNavigation : NavigationManager
    {
        public List<string> Visits = [];
        public DashboardNavigation(string path) => Initialize("http://localhost:7700/", "http://localhost:7700" + path);
        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            Visits.Add(uri);
            Uri = ToAbsoluteUri(uri).AbsoluteUri;
        }
    }

    [Fact]
    public void Anthropic_converts_images_tool_calls_and_multiple_results()
    {
        var messages = JsonNode.Parse("""
            [{"role":"system","content":"system"},
             {"role":"user","content":[{"type":"text","text":"look"},{"type":"image_url","image_url":{"url":"data:image/png;base64,AAAA"}}]},
             {"role":"assistant","content":null,"tool_calls":[{"id":"a","function":{"name":"list_jobs","arguments":"{}"}},{"id":"b","function":{"name":"list_schedules","arguments":"{}"}}]},
             {"role":"tool","tool_call_id":"a","content":"[]"},{"role":"tool","tool_call_id":"b","content":"[]"}]
            """)!.AsArray();
        var request = new MessagesProtocol().Request("claude-test", messages, []);
        Assert.Equal("system", request["system"]!.GetValue<string>());
        Assert.Equal("image", request["messages"]![0]!["content"]![1]!["type"]!.GetValue<string>());
        Assert.Equal("image/png", request["messages"]![0]!["content"]![1]!["source"]!["media_type"]!.GetValue<string>());
        Assert.Equal(3, request["messages"]!.AsArray().Count);
        Assert.Equal(2, request["messages"]![2]!["content"]!.AsArray().Count);
        Assert.Equal("tool_result", request["messages"]![2]!["content"]![0]!["type"]!.GetValue<string>());
        var response = new MessagesProtocol().Response(JsonNode.Parse("""{"content":[{"type":"text","text":"Checking"},{"type":"tool_use","id":"c","name":"list_jobs","input":{}}]}""")!.AsObject());
        Assert.Equal("Checking", response["content"]!.GetValue<string>());
        Assert.Equal("{}", response["tool_calls"]![0]!["function"]!["arguments"]!.GetValue<string>());
    }

    [Fact]
    public void Incomplete_model_responses_cannot_execute_tools()
    {
        Assert.Throws<InvalidOperationException>(() => new MessagesProtocol().Response(new() { ["stop_reason"] = "max_tokens" }));
        Assert.Throws<InvalidOperationException>(() => new ChatCompletionsProtocol().Response(JsonNode.Parse("""{"choices":[{"finish_reason":"length"}]}""")!.AsObject()));
    }

    [Theory]
    [InlineData("file:///tmp/model")]
    [InlineData("https://key@example.com/v1")]
    [InlineData("https://example.com/v1?key=secret")]
    [InlineData("https://example.com/v1#fragment")]
    public void Model_endpoints_reject_unsafe_URL_shapes(string url) =>
        Assert.Throws<ArgumentException>(() => ModelConnections.Validate(new(Guid.NewGuid(), "test", "openai", url, ["model"], true)));

    [Theory]
    [InlineData("codex", "cli://codex", null)]
    [InlineData("claude-code", "cli://claude", null)]
    public void Local_cli_connections_use_fixed_commands_without_api_keys(string protocol, string endpoint, string? key)
    {
        ModelConnections.Validate(new(Guid.NewGuid(), "Local CLI", protocol, endpoint, ["default"], true, key));
        Assert.Throws<ArgumentException>(() => ModelConnections.Validate(new(Guid.NewGuid(), "CLI", protocol, "cli:///tmp/arbitrary-command", ["default"], true)));
        Assert.Throws<ArgumentException>(() => ModelConnections.Validate(new(Guid.NewGuid(), "CLI", protocol, endpoint, ["default"], true, "secret")));
    }

    [Fact]
    public async Task Host_cli_accounts_are_hidden_and_denied_outside_the_default_administrator()
    {
        await using var fixture = new Fixture();
        fixture.Tenant.Slug = "default";
        var id = Guid.NewGuid();
        await fixture.Connections.SaveAsync(new(id, "Codex", "codex", "cli://codex", ["test"], true));
        Assert.True(Assert.Single(await fixture.Connections.ListAsync()).IsLocalCli);
        ((MembersProxy)fixture.Members).Allowed = false;
        Assert.Empty(await fixture.Connections.ListAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Connections.CompleteAsync(id, "test", [], [], default));
        ((MembersProxy)fixture.Members).Allowed = true;
        fixture.Tenant.Slug = "other-workspace";
        Assert.Empty(await fixture.Connections.ListAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Connections.CompleteAsync(id, "test", [], [], default));
    }

    [Fact]
    public async Task History_is_encrypted_private_project_scoped_and_leased()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var conversation = new BuildConversation { ProjectId = fixture.ProjectId, Title = "Private build" };
        var lease = await fixture.Store.AcquireAsync(conversation);
        conversation.Messages.Add(new JsonObject { ["role"] = "user", ["content"] = "private request" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.AcquireAsync(conversation));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.SaveAsync(conversation, Guid.NewGuid(), false));
        await fixture.Store.SaveAsync(conversation, lease, true);
        await fixture.Store.SaveMemoryAsync(fixture.ProjectId, "Prefer Python");
        using (var scope = fixture.Provider.CreateScope())
        {
            var row = await scope.ServiceProvider.GetRequiredService<AppDbContext>().BuildConversations.SingleAsync();
            Assert.DoesNotContain("private", row.Content);
            Assert.StartsWith(DataProtectionEncryptor.Prefix, row.Content);
        }
        Assert.Single(await fixture.Store.ListAsync(fixture.ProjectId));
        var stale = new BuildConversation { Id = conversation.Id, ProjectId = fixture.ProjectId };
        var secondLease = await fixture.Store.AcquireAsync(stale);
        Assert.Single(stale.Messages);
        await fixture.Store.SaveAsync(stale, secondLease, true);
        var originalUser = fixture.User.UserId;
        fixture.User.UserId = Guid.NewGuid();
        Assert.Empty(await fixture.Store.ListAsync(fixture.ProjectId));
        Assert.Equal("", await fixture.Store.GetMemoryAsync(fixture.ProjectId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.LoadAsync(fixture.ProjectId, conversation.Id));
        fixture.User.UserId = originalUser;
        var originalTenant = fixture.Tenant.TenantId;
        fixture.Tenant.TenantId = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.LoadAsync(fixture.ProjectId, conversation.Id));
        fixture.Tenant.TenantId = originalTenant;
        Assert.Equal("Prefer Python", await fixture.Store.GetMemoryAsync(fixture.ProjectId));
        await fixture.Store.DeleteAsync(fixture.ProjectId, conversation.Id);
        Assert.Empty(await fixture.Store.ListAsync(fixture.ProjectId));
        Assert.Equal("Prefer Python", await fixture.Store.GetMemoryAsync(fixture.ProjectId));
    }

    [Fact]
    public async Task Model_connections_encrypt_keys_enforce_admin_and_do_not_forward_keys_to_changed_endpoints()
    {
        await using var fixture = new Fixture();
        var id = Guid.NewGuid();
        var draft = new SaveModelConnection(id, "Local", "openai", "https://model.test/v1", ["test"], true, "secret-key");
        await fixture.Connections.SaveAsync(draft);
        using (var scope = fixture.Provider.CreateScope())
        {
            var row = await scope.ServiceProvider.GetRequiredService<AppDbContext>().ModelConnections.SingleAsync();
            Assert.DoesNotContain("secret-key", row.ApiKey);
        }
        Assert.True(Assert.Single(await fixture.Connections.ListAsync()).HasApiKey);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Connections.SaveAsync(draft with { BaseUrl = "https://other.test/v1", ApiKey = null }));
        await fixture.Connections.SaveAsync(draft with
        {
            ApiKey = null
        });
        var reply = await fixture.Connections.CompleteAsync(id, "test", [], [], default);
        Assert.Equal("OK", reply["content"]!.GetValue<string>());
        Assert.Equal("Bearer secret-key", fixture.Http.Authorization);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Connections.CompleteAsync(id, "unconfigured", [], [], default));
        await fixture.Connections.SaveAsync(draft with
        {
            Enabled = false,
            ApiKey = null
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Connections.CompleteAsync(id, "test", [], [], default));
        fixture.Permissions.Allowed = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Connections.SaveAsync(draft));
        fixture.Permissions.Allowed = true;
        fixture.Tenant.TenantId = Guid.NewGuid();
        Assert.Empty(await fixture.Connections.ListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Connections.CompleteAsync(id, "test", [], [], default));
    }

    [Fact]
    public async Task Build_tools_reject_missing_permission_and_foreign_project_jobs()
    {
        var permissions = new Permissions { Allowed = false };
        var projectId = Guid.NewGuid();
        var dispatcher = new ToolDispatcher(projectId);
        var service = new PlaceContextService(dispatcher, null!, null!, null!);
        var tools = new BuildTools(projectId, service, permissions, null!);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.delete_job(Guid.NewGuid(), default));
        permissions.Allowed = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => tools.delete_job(Guid.NewGuid(), default));
        Assert.Equal(0, dispatcher.Commands);
        var functions = tools.Functions();
        Assert.Contains(functions, f => f.Name == "save_workflow");
        Assert.Contains(functions, f => f.Name == "save_job");
        Assert.All(functions, f => Assert.True(f.JsonSchema.TryGetProperty("properties", out _)));
    }

    [Fact]
    public async Task New_messages_require_text_and_enforce_the_length_limit()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var agent = fixture.Provider.GetRequiredService<BuildChatAgent>();
        var conversation = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test" };
        foreach (var invalid in new[] { "", " \t\n", new string('x', 16001) })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => agent.QueueAsync(conversation, invalid));
        }
        Assert.Empty(conversation.Messages);
        var text = new string('x', 16000);
        await agent.QueueAsync(conversation, text);
        var content = conversation.Messages[0]!["content"]!.AsArray();
        Assert.Equal("text", Assert.Single(content)!["type"]!.GetValue<string>());
        Assert.Equal(text, content[0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Agent_executes_tool_round_trip_and_persists_history()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var gateway = new ScriptedModel();
        var operations = new OperationCenter(null!, default);
        var agent = new BuildChatAgent(fixture.Store, gateway, fixture.Service, fixture.Permissions, fixture.Tenant,
            operations: operations, user: fixture.User);
        var conversation = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test" };
        await agent.QueueAsync(conversation, "How do jobs work?");
        Assert.Equal(BuildRunStatus.Queued, (await fixture.Store.LoadAsync(fixture.ProjectId, conversation.Id)).Status);
        var operation = Assert.Single(operations.ListForTenant(fixture.Tenant.TenantId, fixture.User.UserId));
        Assert.Contains($"conversation={conversation.Id}", operation.Link);
        Assert.Empty(operations.ListForTenant(fixture.Tenant.TenantId));
        await agent.RunAsync(conversation, default);
        Assert.Equal(PortalOperationStatus.Succeeded, operation.Status);
        Assert.Equal(2, gateway.Requests);
        Assert.Equal(new[] { "user", "assistant", "tool", "assistant" }, conversation.Messages.Select(x => x!["role"]!.GetValue<string>()));
        Assert.Equal(4, (await fixture.Store.LoadAsync(fixture.ProjectId, conversation.Id)).Messages.Count);
        var lease = await fixture.Store.AcquireAsync(conversation);
        await fixture.Store.SaveAsync(conversation, lease, true);
    }

    [Fact]
    public void Interrupted_tool_calls_receive_unknown_outcome_instead_of_being_replayed()
    {
        var messages = JsonNode.Parse("""[{"role":"assistant","tool_calls":[{"id":"a"},{"id":"b"}]},{"role":"tool","tool_call_id":"a","content":"done"}]""")!.AsArray();
        BuildChatAgent.RepairInterruptedCalls(messages);
        Assert.Equal(3, messages.Count);
        Assert.Equal("b", messages[2]!["tool_call_id"]!.GetValue<string>());
        Assert.Contains("unknown", messages[2]!["content"]!.GetValue<string>());
        BuildChatAgent.RepairInterruptedCalls(messages);
        Assert.Equal(3, messages.Count);
    }

    [Fact]
    public async Task Clickable_answers_persist_and_send_once_without_overwriting_a_draft()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var gateway = new ScriptedModel("""{"tool_calls":[{"id":"q","type":"function","function":{"name":"ask_user","arguments":"{\"question\":\"Which language?\",\"answers\":[\" Use C# \",\"Use Python\"]}"}}]}""");
        var agent = new BuildChatAgent(fixture.Store, gateway, fixture.Service, fixture.Permissions, fixture.Tenant);
        var conversation = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test" };
        await agent.QueueAsync(conversation, "Help me choose a language");
        await agent.RunAsync(conversation, default);
        Assert.Equal(1, gateway.Requests);
        Assert.Equal(BuildRunStatus.Completed, conversation.Status);
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, agent, new PortalUiState(), new NoJs(), fixture.Service, fixture.Permissions);
        try
        {
            await vm.LoadAsync(fixture.ProjectId);
            await vm.OpenAsync(conversation.Id);
            var question = vm.Conversation.Messages.Last()!;
            Assert.Equal("Which language?", BuildChatViewModel.Text(question));
            Assert.Equal(new[] { "Use C#", "Use Python" }, BuildChatViewModel.SuggestedAnswers(question));
            Assert.True(vm.CanAnswer(question));
            await vm.AnswerAsync(question, "Unexpected choice");
            Assert.Equal("", vm.Draft);
            vm.Draft = "My custom answer";
            await vm.AnswerAsync(question, "Use C#");
            Assert.Equal("My custom answer", vm.Draft);
            vm.Draft = "";
            await vm.AnswerAsync(question, "Use C#");
            await vm.AnswerAsync(question, "Use C#");
            Assert.Equal(BuildRunStatus.Queued, vm.Conversation.Status);
            Assert.Equal("Use C#", BuildChatViewModel.Text(vm.Conversation.Messages.Last()!));
            Assert.Equal(2, vm.Conversation.Messages.Count(x => x?["role"]?.GetValue<string>() == "user"));
            Assert.False(vm.CanAnswer(question));
            await agent.RunAsync(vm.Conversation, default);
            Assert.False(vm.CanAnswer(question));
            Assert.Equal(2, gateway.Requests);
        }
        finally
        {
            await vm.DisposePageAsync();
        }
    }

    [Theory]
    [InlineData("", "[\"Yes\",\"No\"]")]
    [InlineData("Choose", "[]")]
    [InlineData("Choose", "[\"Yes\"]")]
    [InlineData("Choose", "[\"Yes\",\" yes \"]")]
    [InlineData("Choose", "[\"Yes\",null]")]
    [InlineData("Choose", "[\"1\",\"2\",\"3\",\"4\",\"5\",\"6\",\"7\"]")]
    public void Clickable_answers_validate_model_input(string question, string answers)
    {
        var tools = new BuildTools(Guid.NewGuid(), null!, null!, null!);
        Assert.Throws<ArgumentException>(() => tools.ask_user(question, JsonSerializer.Deserialize<string[]>(answers)!));
        Assert.Throws<ArgumentException>(() => tools.ask_user(new string('x', 4001), ["Yes", "No"]));
        Assert.Throws<ArgumentException>(() => tools.ask_user("Choose", [new string('x', 201), "No"]));
    }

    [Fact]
    public async Task Question_cannot_be_combined_with_an_action()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var gateway = new ScriptedModel("""{"tool_calls":[{"id":"q","function":{"name":"ask_user","arguments":"{\"question\":\"Proceed?\",\"answers\":[\"Yes\",\"No\"]}"}},{"id":"m","function":{"name":"remember","arguments":"{\"memory\":\"Must not be saved\"}"}}]}""");
        var agent = new BuildChatAgent(fixture.Store, gateway, fixture.Service, fixture.Permissions, fixture.Tenant);
        var conversation = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test" };
        await agent.QueueAsync(conversation, "Ask before proceeding");
        await agent.RunAsync(conversation, default);
        Assert.Equal(BuildRunStatus.Failed, conversation.Status);
        Assert.Equal("", await fixture.Store.GetMemoryAsync(fixture.ProjectId));
        Assert.DoesNotContain(conversation.Messages, x => x?["suggested_answers"] is not null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_or_cancelled_model_turn_is_saved_and_releases_its_lease(bool cancelled)
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var agent = new BuildChatAgent(fixture.Store, new FailingModel(cancelled), fixture.Service, fixture.Permissions, fixture.Tenant);
        var conversation = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test" };
        await agent.QueueAsync(conversation, "Help");
        await agent.RunAsync(conversation, default);
        var saved = await fixture.Store.LoadAsync(fixture.ProjectId, conversation.Id);
        Assert.Equal("user", saved.Messages[0]!["role"]!.GetValue<string>());
        Assert.Equal(BuildRunStatus.Failed, saved.Status);
        if (cancelled)
        {
            Assert.Contains("without a Build stop request", saved.Error);
        }
        Assert.NotNull(saved.Error);
        var lease = await fixture.Store.AcquireAsync(saved);
        await fixture.Store.SaveAsync(saved, lease, true);
    }

    [Fact]
    public void Workflow_round_trip_preserves_actions_gates_and_branches()
    {
        var jobId = Guid.NewGuid();
        var view = new JobChainStageView([], new ConditionGateView("exists:approved"),
            [new JobChainStageView([new(jobId, "fallback")], new WaitGateView(30))],
            new VirtualMachineChainActionView("linux", "Inspect the form", "result", 2048, 2, 120, true));
        var modelInput = WorkflowStage.FromView(view);
        var json = JsonSerializer.Serialize(modelInput);
        var stage = JsonSerializer.Deserialize<WorkflowStage>(json)!.ToStage();
        var action = Assert.IsType<VirtualMachineChainAction>(stage.Action);
        Assert.Equal("Inspect the form", action.Command);
        Assert.Equal(2048, action.MemoryMb);
        var condition = Assert.IsType<ConditionGate>(stage.Gate);
        Assert.Equal(jobId, Assert.Single(Assert.Single(condition.ElseBranch!).JobIds));
        Assert.Equal(TimeSpan.FromSeconds(30), Assert.IsType<WaitGate>(condition.ElseBranch![0].Gate).Duration);
    }

    [Theory]
    [InlineData(null, "Dashboard", null)]
    [InlineData("""{"workspace":[{"id":"build.chat","label":"Build","order":10,"visible":true}]}""", "Dashboard", null)]
    [InlineData("""{"workspace":[{"id":"build.chat","label":"Chat","order":10,"visible":true,"section":"Build"}]}""", "Dashboard", null)]
    [InlineData("""{"workspace":[{"id":"build.chat","label":"Assistant","order":10,"visible":true,"section":"Custom"}]}""", "Assistant", "Custom")]
    [InlineData("""{"workspace":[{"id":"chains","order":0,"visible":true,"section":"Automation"},{"id":"jobs","order":10,"visible":true,"section":"Automation"}]}""", "Dashboard", null)]
    [InlineData("""{"workspace":[{"id":"chains","order":0,"visible":true,"section":"Platform"},{"id":"jobs","order":10,"visible":true,"section":"Platform"}]}""", "Dashboard", null)]
    public async Task Dashboard_menu_is_a_single_link_and_preserves_custom_labels(string? menuJson, string label, string? section)
    {
        await using var fixture = new Fixture();
        using (var scope = fixture.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Tenants.Add(new()
            {
                Id = fixture.Tenant.TenantId,
                Name = "Test",
                Slug = "test",
                MenuJson = menuJson
            });
            await db.SaveChangesAsync();
        }
        var service = new MenuConfigService(fixture.Provider.GetRequiredService<IServiceScopeFactory>(),
            fixture.Tenant, fixture.Permissions, fixture.User);
        var items = await service.GetWorkspaceMenuAsync(fixture.ProjectId);
        var build = Assert.Single(items, x => x.Id == "build.chat");
        Assert.Equal(label, build.Label);
        Assert.Equal("link", build.Kind);
        Assert.Equal(section, build.Section);
        Assert.Equal($"/project/{fixture.ProjectId}/dashboard", build.Href);
        if (section is null)
        {
            Assert.Null(build.Parent);
        }
        Assert.DoesNotContain(items, x => x.Kind == "group" && x.Label == "Build");
        Assert.DoesNotContain(items, x => x.Label is "Automation" or "Platform" || x.Section is "Automation" or "Platform");
        Assert.Equal("section:Infrastructure", Assert.Single(items, x => x.Id == "jobs").Parent);
    }

    [Fact]
    public void Task_hints_fill_an_editable_draft_without_sending_or_overwriting_work()
    {
        var vm = new BuildChatViewModel(null!, null!, null!, new PortalUiState(), new NoJs(), null!, null!);
        const string prompt = "Help me connect a data source.";
        Assert.True(vm.ApplyTaskHint(prompt));
        Assert.True(vm.ChatOpen);
        Assert.Equal(prompt, vm.Draft);
        Assert.Empty(vm.Conversation.Messages);
        Assert.False(vm.Busy);
        vm.Draft += " My extra instructions.";
        Assert.False(vm.ApplyTaskHint("A different task"));
        Assert.EndsWith(" My extra instructions.", vm.Draft);
        vm.Draft = "";
        vm.Conversation.Status = BuildRunStatus.Running;
        Assert.False(vm.ApplyTaskHint(prompt));
        Assert.Equal("", vm.Draft);
    }

    [Fact]
    public async Task Dashboard_starts_with_tiles_and_opens_chat_only_after_selection()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections,
            fixture.Provider.GetRequiredService<BuildChatAgent>(), new PortalUiState(), new NoJs(), fixture.Service, fixture.Permissions);
        var services = new ServiceCollection().AddLogging().AddSingleton(vm).AddSingleton<IJSRuntime, NoJs>().AddTransient<RunOutputViewModel>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        async Task<string> RenderAsync() => await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<PlaceContext.Host.Components.Pages.BuildChat>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["ProjectId"] = fixture.ProjectId }));
            return rendered.ToHtmlString();
        });
        var html = await RenderAsync();
        Assert.False(vm.ChatOpen);
        Assert.DoesNotContain("class=\"chat-entry-title\"", html);
        Assert.DoesNotContain("Choose a task", html);
        Assert.Contains("Run a workflow", html);
        Assert.DoesNotContain("dashboard-row-drop", html);
        Assert.DoesNotContain("draggable=", html);
        Assert.Contains("dashboard-drag-handle", html);
        Assert.Contains("dashboard-resize", html);
        Assert.Contains("Remove Run a workflow from dashboard", html);
        Assert.DoesNotContain("Arrange tiles", html);
        Assert.DoesNotContain("aria-label=\"Agent settings\"", html);
        Assert.Contains("Job statistics", html);
        Assert.Contains("Ask a question", html);
        Assert.DoesNotContain("Customize dashboard", html);
        Assert.Contains("Add chart", html);
        Assert.DoesNotContain("thinking-dots", html);
        Assert.DoesNotContain("dashboard-arrange-hint", html);
        Assert.Contains("class=\"chat-entry\" hidden", html);
        Assert.DoesNotContain("Quick picks", html);
        Assert.DoesNotContain("aria-label=\"Runs\"", html);
        Assert.DoesNotContain("aria-label=\"Charts\"", html);
        Assert.DoesNotContain("aria-label=\"Data\"", html);
        Assert.DoesNotContain("aria-label=\"Project memory\"", html);
        Assert.DoesNotContain("MCP tools", html);
        vm.ShowHistory = true;
        html = await RenderAsync();
        Assert.Contains("aria-label=\"Sessions\"", html);
        Assert.Contains("New session", html);
        vm.ShowHistory = false;
        vm.OpenChat();
        Assert.Empty(vm.Conversation.Messages);
        Assert.Empty(vm.Draft);
        html = await RenderAsync();
        Assert.Contains("<h2 class=\"chat-entry-title\"", html);
        Assert.Contains("How can I help?", html);
        Assert.Contains("Explore your project, create a chart, or get help with a workflow.", html);
        Assert.DoesNotContain("What do you need to do?", html);
        Assert.DoesNotContain("class=\"dashboard-actions\"", html);
        Assert.DoesNotContain("class=\"chat-entry\" hidden", html);
        Assert.Contains("aria-label=\"Message assistant\"", html);
        await vm.NewAsync();
        Assert.Contains("class=\"chat-entry\" hidden", await RenderAsync());
        const string prompt = "Show this project's job statistics.";
        await vm.StartDashboardActionAsync(prompt);
        html = await RenderAsync();
        Assert.True(vm.ChatOpen);
        Assert.Equal(BuildRunStatus.Queued, vm.Conversation.Status);
        Assert.Equal(prompt, BuildChatViewModel.Text(Assert.Single(vm.Conversation.Messages)!));
        Assert.DoesNotContain("class=\"chat-entry-title\"", html);
        Assert.DoesNotContain("class=\"dashboard-actions\"", html);
        Assert.DoesNotContain("class=\"chat-entry\" hidden", html);
        Assert.Contains("Waiting for the assistant", html);
        Assert.Contains("class=\"thinking-dots\" aria-hidden=\"true\"", html);
        vm.Conversation.Status = BuildRunStatus.Running;
        Assert.Contains("Assistant is working", await RenderAsync());
        foreach (var status in new[] { BuildRunStatus.Completed, BuildRunStatus.Failed, BuildRunStatus.Stopped, BuildRunStatus.Interrupted })
        {
            vm.Conversation.Status = status;
            Assert.DoesNotContain("thinking-dots", await RenderAsync());
        }
        await vm.DisposePageAsync();
    }

    [Fact]
    public async Task Sending_first_message_opens_workspace_and_new_chat_restores_welcome()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections,
            fixture.Provider.GetRequiredService<BuildChatAgent>(), new PortalUiState(), new NoJs(), fixture.Service, fixture.Permissions);
        var services = new ServiceCollection().AddLogging().AddSingleton(vm).AddSingleton<IJSRuntime, NoJs>().AddTransient<RunOutputViewModel>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<PlaceContext.Host.Components.Pages.BuildChat>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["ProjectId"] = fixture.ProjectId }));
            Assert.DoesNotContain("conversation-active", rendered.ToHtmlString());
            vm.Draft = "Inspect this project.";
            await vm.SendAsync();
            var html = rendered.ToHtmlString();
            Assert.Contains("conversation-active", html);
            Assert.Contains("aria-label=\"Dashboard workspace\"", html);
            Assert.Contains("Inspect this project.", html);
            Assert.DoesNotContain("What do you need to do?", html);
            Assert.Contains("aria-label=\"Close workspace\"", html);
            vm.CloseWorkspace();
            Assert.False(vm.WorkspaceVisible);
            var closed = await renderer.RenderComponentAsync<PlaceContext.Host.Components.Pages.BuildChat>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["ProjectId"] = fixture.ProjectId }));
            Assert.DoesNotContain("conversation-active", closed.ToHtmlString());
            Assert.Contains("aria-label=\"Open workspace\"", closed.ToHtmlString());
            Assert.Contains("Inspect this project.", closed.ToHtmlString());
            vm.OpenWorkspace();
            Assert.True(vm.WorkspaceVisible);
            await vm.NewAsync();
            Assert.Empty(vm.Conversation.Messages);
            Assert.Empty(vm.Draft);
            Assert.False(vm.WorkspaceVisible);
            vm.Conversation.Messages.Add(new JsonObject { ["role"] = "user", ["content"] = "Another conversation" });
            Assert.True(vm.WorkspaceVisible);
        });
    }

    [Theory]
    [InlineData("{\"jobs\":[1]}", true)]
    [InlineData("```json\n{\"jobs\":[1]}\n```", true)]
    [InlineData("```\r\n[1,2]\r\n```", true)]
    [InlineData("{not valid JSON}", false)]
    [InlineData("Ordinary reply", false)]
    [InlineData("```python\n[1,2]\n```", false)]
    [InlineData("```", false)]
    public void Assistant_json_replies_are_pretty_printed_without_reinterpreting_plain_text(string content, bool expected)
    {
        var message = new JsonObject { ["role"] = "assistant", ["content"] = content };
        var json = BuildChatViewModel.JsonReply(message);
        Assert.Equal(expected, json is not null);
        if (json is not null)
        {
            Assert.NotNull(JsonNode.Parse(json));
            Assert.Contains("\n", json);
        }
        message["role"] = "user";
        Assert.Null(BuildChatViewModel.JsonReply(message));
    }

    [Fact]
    public void Assistant_markdown_formats_reports_tables_and_code()
    {
        var html = BuildChatViewModel.FormatReply("""
            ## Seller report

            Please send the **reporting date range** (Brisbane time) and **anonymised seller records**.

            Useful fields:
            - Anonymous case ID and intake date
            - Onboarding status
            - Missing documents or evidence
            - Approval status and decision date

            Mark unknown values as “unknown,” not zero.

            | Case | Status |
            | --- | --- |
            | A1 | unknown |

            1. Review `A1`.
            2. Produce the report.

            ```csv
            case,status
            A1,unknown
            ```

            [Documentation](https://example.com/report)
            """);
        Assert.Contains("<h2>Seller report</h2>", html);
        Assert.Contains("<strong>reporting date range</strong>", html);
        Assert.Contains("<li>Onboarding status</li>", html);
        Assert.Contains("<table>", html);
        Assert.Contains("<td>unknown</td>", html);
        Assert.Contains("<ol>", html);
        Assert.Contains("<code>A1</code>", html);
        Assert.Contains("<pre><code class=\"language-csv\">", html);
        Assert.Contains("href=\"https://example.com/report\"", html);
    }

    [Theory]
    [InlineData("https://example.com/report")]
    [InlineData("/project/example/dashboard")]
    [InlineData("../reports")]
    [InlineData("#summary")]
    [InlineData("mailto:reports@example.com")]
    public void Assistant_markdown_preserves_safe_links(string url)
    {
        Assert.Contains($"href=\"{url}\"", BuildChatViewModel.FormatReply($"[Report]({url})"));
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("[click](javascript:alert%281%29)")]
    [InlineData("[click](jav&#x61;script:alert%281%29)")]
    [InlineData("[click](data:text/html;base64,PHNjcmlwdD4=)")]
    [InlineData("[click](java&#9;script:alert%281%29)")]
    [InlineData("![image](javascript:alert%281%29)")]
    public void Assistant_markdown_does_not_render_active_HTML_or_unsafe_links(string content)
    {
        var html = BuildChatViewModel.FormatReply(content);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img src=x", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=\"javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=\"data:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("src=\"javascript:", html, StringComparison.OrdinalIgnoreCase);
        if (content.Contains("["))
        {
            Assert.Contains("=\"\"", html);
        }
    }

    [Fact]
    public async Task Chat_renders_agent_selection_corgi_and_escapes_assistant_HTML()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var connectionId = Guid.NewGuid();
        await fixture.Connections.SaveAsync(new(connectionId, "Test provider", "openai", "http://localhost:11434/v1", ["test-model"], true));
        var profile = new DashboardAgentProfile { Name = "Report assistant", ConnectionId = connectionId, Model = "test-model" };
        var settings = new DashboardSettings();
        settings.Agents.Add(profile);
        await fixture.Store.SaveSettingsAsync(fixture.ProjectId, settings);
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, null!, new PortalUiState(), new NoJs(), fixture.Service, fixture.Permissions);
        await vm.LoadAsync(fixture.ProjectId);
        vm.SelectAgent(profile.Id.ToString());
        vm.Conversation.Messages.Add(new JsonObject { ["role"] = "user", ["content"] = "**Keep my input literal**" });
        vm.Conversation.Messages.Add(JsonNode.Parse("""{"role":"user","content":[{"type":"image_url","image_url":{"url":"data:image/png;base64,AAAA"}}]}"""));
        vm.Conversation.Messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = "Please send the **reporting date range**.\n\n- Onboarding status\n- Missing documents" });
        vm.Conversation.Messages.Add(new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = "<script>alert(1)</script>",
            ["suggested_answers"] = new JsonArray("<script>answer</script>", "Use Python")
        });
        vm.Conversation.Messages.Add(new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = "{\"ready\":true}",
            ["tool_calls"] = JsonNode.Parse("""[{"id":"inspect","function":{"name":"list_jobs","arguments":"{}"}}]""")
        });
        vm.Conversation.Messages.Add(new JsonObject { ["role"] = "tool", ["content"] = "{\"jobs\":[{\"name\":\"Example\"}]}" });
        var services = new ServiceCollection().AddLogging().AddSingleton(vm).AddSingleton<IJSRuntime, NoJs>().AddTransient<RunOutputViewModel>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<PlaceContext.Host.Components.Pages.BuildChat>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["ProjectId"] = fixture.ProjectId }));
            return rendered.ToHtmlString();
        });
        Assert.Contains("Report assistant", html);
        Assert.Contains("class=\"assistant-avatar\"", html);
        Assert.Contains("class=\"corgi-mark\"", html);
        Assert.DoesNotContain("chat-provider", html);
        Assert.DoesNotContain("chat-model", html);
        Assert.DoesNotContain("Attach images", html);
        Assert.DoesNotContain("type=\"file\"", html);
        Assert.Contains("src=\"data:image/png;base64,AAAA\"", html);
        Assert.Contains("class=\"chat-input\"", html);
        Assert.Contains("aria-label=\"Message assistant\"", html);
        Assert.DoesNotContain("contenteditable", html);
        Assert.DoesNotContain("MCP tools", html);
        Assert.DoesNotContain("class=\"quick-picks\"", html);
        Assert.Contains("Dictate", html);
        Assert.DoesNotContain("aria-label=\"Project memory\"", html);
        Assert.DoesNotContain("aria-label=\"Runs\"", html);
        Assert.DoesNotContain("aria-label=\"Charts\"", html);
        Assert.DoesNotContain("aria-label=\"Data\"", html);
        Assert.DoesNotContain("<aside", html);
        Assert.Contains("aria-label=\"History\"", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("<strong>reporting date range</strong>", html);
        Assert.Contains("<li>Onboarding status</li>", html);
        Assert.Contains("**Keep my input literal**", html);
        Assert.DoesNotContain("<strong>Keep my input literal</strong>", html);
        Assert.Contains("aria-label=\"Suggested answers\"", html);
        Assert.Contains("&lt;script&gt;answer&lt;/script&gt;</button>", html);
        Assert.Contains("Use Python</button>", html);
        Assert.Contains("aria-label=\"Action result JSON\"", html);
        Assert.Contains("aria-label=\"Tool arguments JSON\"", html);
        Assert.Contains("aria-label=\"Assistant JSON response\"", html);
        Assert.Contains("class=\"run-output\"", html);
        Assert.DoesNotContain("<pre>", html);
    }
    [Theory]
    [InlineData("http://localhost:11434/v1", true, true)]
    [InlineData("http://127.0.0.1:1234/v1", true, true)]
    [InlineData("http://[::1]:1234/v1", true, true)]
    [InlineData("cli://codex", true, true)]
    [InlineData("cli://claude", true, true)]
    [InlineData("http://localhost:11434/v1", false, true)]
    [InlineData("http://localhost:11434/v1", true, false)]
    public async Task Chat_prefers_local_models_and_preserves_explicit_choices(string url, bool enabled, bool hasModels)
    {
        await using var fixture = new Fixture();
        fixture.Tenant.Slug = "default";
        await fixture.SeedAsync();
        var remoteId = Guid.NewGuid();
        var localId = fixture.ConnectionId;
        await fixture.Connections.SaveAsync(new(remoteId, "A remote", "openai", "https://example.com/v1", ["remote-model"], true));
        var protocol = url == "cli://codex" ? "codex" : url == "cli://claude" ? "claude-code" : "openai";
        await fixture.Connections.SaveAsync(new(localId, "Z local", protocol, url, hasModels ? ["local-model"] : [], enabled));
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, null!, new PortalUiState(), new NoJs(), fixture.Service, fixture.Permissions);
        try
        {
            await vm.LoadAsync(fixture.ProjectId);
            Assert.Null(vm.Error);
            Assert.Equal(enabled && hasModels ? localId : remoteId, vm.Conversation.ConnectionId);
            Assert.Equal(enabled && hasModels ? "local-model" : "remote-model", vm.Conversation.Model);
            var profile = new DashboardAgentProfile { Name = "Remote", ConnectionId = remoteId, Model = "remote-model" };
            vm.Dashboard.Agents.Add(profile);
            vm.SelectAgent(profile.Id.ToString());
            await vm.NewAsync();
            Assert.Equal(remoteId, vm.Conversation.ConnectionId);
            Assert.Equal("remote-model", vm.Conversation.Model);
        }
        finally
        {
            await vm.DisposePageAsync();
        }
    }

    [Fact]
    public async Task Conversation_receives_worker_progress_without_polling_and_detaches_on_navigation()
    {
        var model = new WaitingModel();
        await using var fixture = new Fixture(model);
        await fixture.SeedAsync();
        var agent = fixture.Provider.GetRequiredService<BuildChatAgent>();
        var store = DispatchProxy.Create<IBuildChatStore, DelayedChatStore>();
        var proxy = (DelayedChatStore)store;
        proxy.Inner = fixture.Store;
        var vm = new BuildChatViewModel(store, fixture.Connections, agent, new PortalUiState(), new NoJs(),
            fixture.Service, fixture.Permissions, operations: fixture.Operations, tenant: fixture.Tenant, user: fixture.User);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.Attach(() =>
        {
            if (vm.Conversation.Status == BuildRunStatus.Completed)
            {
                completed.TrySetResult();
            }
            return Task.CompletedTask;
        });
        try
        {
            await vm.LoadAsync(fixture.ProjectId);
            vm.Draft = "Inspect this project.";
            await vm.SendAsync();
            var id = vm.Conversation.Id;
            var worker = new BuildChatBackgroundService(fixture.Provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<BuildChatBackgroundService>.Instance, fixture.Operations);
            var run = worker.ProcessAsync(await CandidateAsync(fixture, id), default);
            await model.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            vm.CloseWorkspace();
            model.Release.TrySetResult();
            await run.WaitAsync(TimeSpan.FromSeconds(5));
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("Finished.", BuildChatViewModel.Text(vm.Conversation.Messages.Last()!));
            Assert.False(vm.WorkspaceVisible);
            var operation = Assert.Single(fixture.Operations.ListForTenant(fixture.Tenant.TenantId, fixture.User.UserId));
            Assert.EndsWith($"/dashboard?conversation={id}", operation.Link);
            await vm.NewAsync();
            var loads = proxy.TotalLoads;
            fixture.Operations.ReportProgress(operation.Id);
            Assert.Equal(loads, proxy.TotalLoads);
            Assert.Empty(vm.Conversation.Messages);
            await vm.ToggleHistoryAsync();
            Assert.Contains(vm.Conversations, c => c.Id == id && c.Status == BuildRunStatus.Completed);
        }
        finally
        {
            await vm.DisposePageAsync();
        }
    }

    [Fact]
    public async Task Background_run_survives_page_disposal_and_reconnects_to_persisted_progress()
    {
        var model = new WaitingModel();
        await using var fixture = new Fixture(model);
        await fixture.SeedAsync();
        var agent = fixture.Provider.GetRequiredService<BuildChatAgent>();
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, agent, new PortalUiState(), new NoJs(), fixture.Service, fixture.Permissions);
        await vm.LoadAsync(fixture.ProjectId);
        vm.Draft = "Inspect this project.";
        await vm.SendAsync();
        var id = vm.Conversation.Id;
        Assert.True(vm.Busy);
        var worker = new BuildChatBackgroundService(fixture.Provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<BuildChatBackgroundService>.Instance);
        var run = worker.ProcessAsync(await CandidateAsync(fixture, id), default);
        await model.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await vm.DisposePageAsync();
        Assert.False(run.IsCompleted);
        var reopened = new BuildChatViewModel(fixture.Store, fixture.Connections, agent, new PortalUiState(), new NoJs(), fixture.Service, fixture.Permissions);
        await reopened.LoadAsync(fixture.ProjectId);
        Assert.Equal(id, reopened.Conversation.Id);
        Assert.True(reopened.Busy);
        await reopened.NewAsync();
        Assert.False(reopened.Busy);
        Assert.False(run.IsCompleted);
        model.Release.TrySetResult();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        var result = await fixture.Store.LoadAsync(fixture.ProjectId, id);
        Assert.Equal(BuildRunStatus.Completed, result.Status);
        Assert.Equal("Finished.", result.Messages.Last()!["content"]!.GetValue<string>());
        await reopened.DisposePageAsync();
    }

    [Fact]
    public async Task Waiting_model_keeps_its_lease_alive_and_losing_the_lease_stops_it()
    {
        var model = new WaitingModel();
        await using var fixture = new Fixture(model);
        await fixture.SeedAsync();
        var agent = fixture.Provider.GetRequiredService<BuildChatAgent>();
        var conversation = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test" };
        await agent.QueueAsync(conversation, "Help");
        var run = agent.RunAsync(conversation, default);
        await model.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var before = (await CandidateAsync(fixture, conversation.Id)).LeaseUntil;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        while ((await CandidateAsync(fixture, conversation.Id)).LeaseUntil == before)
        {
            await Task.Delay(100, timeout.Token);
        }
        Assert.False(run.IsCompleted);
        Assert.True((await CandidateAsync(fixture, conversation.Id)).LeaseUntil > before);
        await fixture.Store.RequestStopAsync(fixture.ProjectId, conversation.Id);
        await run.WaitAsync(TimeSpan.FromSeconds(15));
        var saved = await fixture.Store.LoadAsync(fixture.ProjectId, conversation.Id);
        Assert.Equal(BuildRunStatus.Stopped, saved.Status);
        Assert.Contains("keep-alive", saved.Error);
    }

    [Fact]
    public async Task Lease_renewal_rejects_wrong_owner_wrong_claim_expired_and_stopped_runs()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var agent = fixture.Provider.GetRequiredService<BuildChatAgent>();
        var conversation = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test" };
        await agent.QueueAsync(conversation, "Help");
        var lease = await fixture.Store.AcquireAsync(conversation);
        Assert.True(await fixture.Store.RenewAsync(fixture.ProjectId, conversation.Id, lease));
        Assert.False(await fixture.Store.RenewAsync(Guid.NewGuid(), conversation.Id, lease));
        Assert.False(await fixture.Store.RenewAsync(fixture.ProjectId, conversation.Id, Guid.NewGuid()));
        await fixture.Store.RequestStopAsync(fixture.ProjectId, conversation.Id);
        Assert.False(await fixture.Store.RenewAsync(fixture.ProjectId, conversation.Id, lease));
        using (var scope = fixture.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.BuildConversations.SingleAsync();
            row.StopRequested = false;
            row.LeaseUntil = DateTimeOffset.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }
        Assert.False(await fixture.Store.RenewAsync(fixture.ProjectId, conversation.Id, lease));
    }

    [Fact]
    public async Task Background_stop_is_persisted_and_pending_work_is_not_replayed()
    {
        var model = new WaitingModel();
        await using var fixture = new Fixture(model);
        await fixture.SeedAsync();
        var agent = fixture.Provider.GetRequiredService<BuildChatAgent>();
        var conversation = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test" };
        await agent.QueueAsync(conversation, "Help");
        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.QueueAsync(conversation, "Duplicate"));
        var worker = new BuildChatBackgroundService(fixture.Provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<BuildChatBackgroundService>.Instance);
        var candidate = await CandidateAsync(fixture, conversation.Id);
        var run = worker.ProcessAsync(candidate, default);
        await model.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Store.RequestStopAsync(fixture.ProjectId, conversation.Id);
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(BuildRunStatus.Stopped, (await fixture.Store.LoadAsync(fixture.ProjectId, conversation.Id)).Status);
        await worker.ProcessAsync(candidate, default);
        Assert.Equal(1, model.Requests);
    }

    [Fact]
    public async Task Queued_run_checks_current_permissions_and_cancellation_before_execution()
    {
        var model = new WaitingModel();
        await using var fixture = new Fixture(model);
        await fixture.SeedAsync();
        var agent = fixture.Provider.GetRequiredService<BuildChatAgent>();
        var conversation = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test" };
        await agent.QueueAsync(conversation, "Help");
        var worker = new BuildChatBackgroundService(fixture.Provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<BuildChatBackgroundService>.Instance);
        var candidate = await CandidateAsync(fixture, conversation.Id);
        fixture.Permissions.Allowed = false;
        await worker.ProcessAsync(candidate, default);
        fixture.Permissions.Allowed = true;
        Assert.Equal(BuildRunStatus.Failed, (await fixture.Store.LoadAsync(fixture.ProjectId, conversation.Id)).Status);
        await agent.QueueAsync(conversation, "Try again");
        await fixture.Store.RequestStopAsync(fixture.ProjectId, conversation.Id);
        await worker.ProcessAsync(await CandidateAsync(fixture, conversation.Id), default);
        Assert.Equal(BuildRunStatus.Stopped, (await fixture.Store.LoadAsync(fixture.ProjectId, conversation.Id)).Status);
        Assert.Equal(0, model.Requests);
    }

    [Fact]
    public async Task Expired_background_claim_is_interrupted_without_reexecuting_tools()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var agent = fixture.Provider.GetRequiredService<BuildChatAgent>();
        var conversation = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test" };
        await agent.QueueAsync(conversation, "Help");
        var lease = await fixture.Store.AcquireAsync(conversation);
        conversation.Messages.Add(JsonNode.Parse("""{"role":"assistant","tool_calls":[{"id":"unknown-action","function":{"name":"run_job","arguments":"{}"}}]}"""));
        await fixture.Store.SaveAsync(conversation, lease, false);
        using (var scope = fixture.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.BuildConversations.SingleAsync()).LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        var restartedOperations = new OperationCenter(null!, default);
        var notifications = 0;
        using var subscription = restartedOperations.Subscribe(fixture.Tenant.TenantId, () => notifications++,
            fixture.User.UserId, fixture.ProjectId, $"build:{conversation.Id}");
        var worker = new BuildChatBackgroundService(fixture.Provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<BuildChatBackgroundService>.Instance, restartedOperations);
        await worker.ProcessAsync(await CandidateAsync(fixture, conversation.Id), default);
        var saved = await fixture.Store.LoadAsync(fixture.ProjectId, conversation.Id);
        Assert.Equal(BuildRunStatus.Interrupted, saved.Status);
        Assert.True(notifications > 0);
        Assert.Equal(PortalOperationStatus.Failed, Assert.Single(restartedOperations.ListForTenant(fixture.Tenant.TenantId, fixture.User.UserId)).Status);
        Assert.Empty(restartedOperations.ListForTenant(fixture.Tenant.TenantId));
        Assert.Contains(saved.Messages, x => x?["role"]?.GetValue<string>() == "tool" && x["content"]!.GetValue<string>().Contains("unknown"));
    }

    [Fact]
    public async Task Dashboard_loads_saved_models_without_probing_providers_and_discovery_is_explicit()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        fixture.Http.ModelsResponse = """{"data":[{"id":"local-model"},{"id":"local-model"},{"id":""}]}""";
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, null!, new PortalUiState(),
            new NoJs(), fixture.Service, fixture.Permissions);
        await vm.LoadAsync(fixture.ProjectId);
        Assert.Equal(new[] { "test" }, Assert.Single(vm.Connections).Models);
        Assert.Equal(0, fixture.Http.Requests);
        Assert.Null(vm.Error);

        Assert.Equal(new[] { "local-model" }, await fixture.Connections.DiscoverAsync(fixture.ConnectionId));
        Assert.Equal(1, fixture.Http.Requests);
        Assert.Equal(new[] { "test" }, Assert.Single(await fixture.Connections.ListAsync()).Models);
        await vm.DisposePageAsync();
    }

    [Fact]
    public async Task Dashboard_session_is_ready_while_live_chart_data_is_still_loading()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var settings = await fixture.Store.GetSettingsAsync(fixture.ProjectId);
        settings.Charts.Add(new(new ChartSpec("bar", "Orders", ["Ready"], [new("Total", [2])]).ToJson(), "Orders")
        {
            Live = true, Sql = "SELECT status, total FROM orders"
        });
        await fixture.Store.SaveSettingsAsync(fixture.ProjectId, settings);
        var dispatcher = new ToolDispatcher(fixture.ProjectId) { PendingData = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, null!, new(), new NoJs(),
            new PlaceContextService(dispatcher, null!, null!, null!), fixture.Permissions);
        var states = new List<bool>();
        vm.Attach(() => { states.Add(vm.Loading); return Task.CompletedTask; });
        try
        {
            await vm.LoadAsync(fixture.ProjectId).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(vm.Loading);
            Assert.False(vm.NavigationLocked);
            Assert.True(vm.ChartsRefreshing);
            Assert.Equal(1, dispatcher.DataQueries);
            Assert.Contains(true, states);
            Assert.False(states.Last());
        }
        finally
        {
            await vm.DisposePageAsync();
            dispatcher.PendingData.SetResult(dispatcher.Data);
        }
    }

    [Fact]
    public async Task Dashboard_loads_history_without_fetching_memory_for_a_removed_editor()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var store = DispatchProxy.Create<IBuildChatStore, DelayedChatStore>();
        var proxy = (DelayedChatStore)store;
        proxy.Inner = fixture.Store;
        proxy.PendingHistory = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new BuildChatViewModel(store, fixture.Connections, null!, new PortalUiState(),
            new NoJs(), fixture.Service, fixture.Permissions);
        var loading = vm.LoadAsync(fixture.ProjectId);
        try
        {
            Assert.False(loading.IsCompleted);
            Assert.True(vm.Loading);
            Assert.Equal(0, proxy.MemoryLoads);
        }
        finally
        {
            proxy.PendingHistory.SetResult([]);
            await loading;
            await vm.DisposePageAsync();
        }
        Assert.False(vm.Loading);
        Assert.Null(vm.Error);
        Assert.Equal(0, proxy.MemoryLoads);
    }

    private static async Task<BuildConversationRow> CandidateAsync(Fixture fixture, Guid id)
    {
        using var scope = fixture.Provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().BuildConversations.AsNoTracking().SingleAsync(x => x.Id == id);
    }

    private sealed class WaitingModel : IBuildModelGateway
    {
        public int Requests;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<JsonObject> CompleteAsync(Guid connectionId, string model, JsonArray messages, JsonArray tools, CancellationToken ct, Action<string>? report = null)
        {
            Requests++;
            Started.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return new()
            {
                ["role"] = "assistant",
                ["content"] = "Finished."
            };
        }
    }

    private sealed class NoJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }
    private sealed class FailingModel(bool cancelled) : IBuildModelGateway
    {
        public Task<JsonObject> CompleteAsync(Guid connectionId, string model, JsonArray messages, JsonArray tools, CancellationToken ct, Action<string>? report = null) =>
            throw (cancelled ? new OperationCanceledException(ct) : new InvalidOperationException("Provider unavailable."));
    }

    private sealed class ScriptedModel(string? firstReply = null) : IBuildModelGateway
    {
        public int Requests;
        public string? SystemPrompt;
        public Task<JsonObject> CompleteAsync(Guid connectionId, string model, JsonArray messages, JsonArray tools, CancellationToken ct, Action<string>? report = null)
        {
            if (messages[0]!["content"]!.ToString().StartsWith("You are the parent agent responsible"))
                return Task.FromResult(new JsonObject { ["content"] = "{\"approved\":true,\"reason\":\"Within scope\"}" });
            Requests++;
            SystemPrompt = messages[0]!["content"]!.GetValue<string>();
            Assert.All(messages.OfType<JsonObject>(), message => Assert.False(message.ContainsKey("suggested_answers")));
            Assert.All(messages.OfType<JsonObject>(), message => Assert.False(message.ContainsKey("dashboard_action")));
            if (Requests == 2)
            {
                Assert.Contains(messages, x => x?["role"]?.GetValue<string>() == "tool");
            }
            return Task.FromResult(JsonNode.Parse(Requests == 1
                            ? firstReply ?? """{"role":"assistant","tool_calls":[{"id":"a","type":"function","function":{"name":"job_authoring_guide","arguments":"{}"}}]}"""
                            : """{"role":"assistant","content":"Here is the contract."}""")!.AsObject());
        }
    }
    [Fact]
    public async Task Jev_selects_real_connectors_and_hands_credentials_to_a_project_scoped_form()
    {
        var projectId = Guid.NewGuid();
        var dispatcher = new ToolDispatcher(projectId);
        var permissions = new Permissions { Allowed = true };
        var tools = new BuildTools(projectId, new PlaceContextService(dispatcher, null!, null!, null!), permissions, null!);
        foreach (var provider in PlaceContext.Application.DataSources.SourceProviders.All)
        {
            var result = JsonSerializer.Serialize(await tools.prepare_data_source(provider.Id, default));
            Assert.Contains($"/project/{projectId}/sources?connect={provider.Id}", result);
            Assert.Contains("Awaiting secure connection", result);
        }
        Assert.Equal(0, dispatcher.Commands);
        await Assert.ThrowsAsync<ArgumentException>(() => tools.prepare_data_source("invented", default));
        permissions.Allowed = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.prepare_data_source("email", default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.source_connectors(default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.list_data_sources(default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.create_data_pipeline(Guid.NewGuid(), "https://example.com", default));
    }

    [Fact]
    public async Task Build_exposes_workflows_and_Jev_without_app_or_database_tools()
    {
        await using var fixture = new Fixture();
        var tools = new BuildTools(fixture.ProjectId, fixture.Service, fixture.Permissions, fixture.Store);
        var names = tools.Functions().Select(f => f.Name).ToArray();
        Assert.Contains("save_workflow", names);
        Assert.Contains("review_workflow_result", names);
        Assert.DoesNotContain(names, name => name.Contains("service") || name.Contains("database"));
        Assert.DoesNotContain("Hosting", JsonSerializer.Serialize(await tools.project_context(default)));
    }

    [Fact]
    public async Task Deep_link_loads_the_requested_chat_and_new_chat_survives_parameter_refresh()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var chat = new BuildConversation { ProjectId = fixture.ProjectId, Title = "Earlier Build" };
        var lease = await fixture.Store.AcquireAsync(chat);
        await fixture.Store.SaveAsync(chat, lease, true);
        var ui = new PortalUiState();
        ui.Set("Jobs", "existing page");
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, null!, ui, new NoJs(), fixture.Service, fixture.Permissions);
        await vm.LoadAsync(fixture.ProjectId, chat.Id);
        Assert.Equal(chat.Id, vm.Conversation.Id);
        Assert.Equal("Dashboard", ui.Title);
        await vm.NewAsync();
        var fresh = vm.Conversation.Id;
        await vm.LoadAsync(fixture.ProjectId, chat.Id);
        Assert.Equal(fresh, vm.Conversation.Id);
        await vm.DisposePageAsync();
    }

    [Fact]
    public void Tool_results_offer_only_recognized_workflow_job_and_artifact_previews()
    {
        var vm = new BuildChatViewModel(null!, null!, null!, new PortalUiState(), new NoJs(), null!, null!);
        var id = Guid.NewGuid();
        var call = new JsonObject { ["id"] = "call-1", ["function"] = new JsonObject { ["name"] = "get_workflow" } };
        vm.Conversation.Messages.Add(new JsonObject { ["role"] = "assistant", ["tool_calls"] = new JsonArray(call) });
        var result = new JsonObject { ["role"] = "tool", ["tool_call_id"] = "call-1", ["content"] = $$"""{"id":"{{id}}","name":"Review"}""" };
        Assert.Equal(new BuildChatViewModel.WorkspaceItem(id, "workflow", "Review"), Assert.Single(vm.ResultItems(result)));
        call["function"]!["name"] = "run_job";
        Assert.Empty(vm.ResultItems(result));
        call["function"]!["name"] = "list_artifacts";
        var runId = Guid.NewGuid();
        result["content"] = $$"""[{"id":"{{id}}","runId":"{{runId}}","title":"Report.pdf","downloadUrl":"https://untrusted.test/file"}]""";
        Assert.Equal(new BuildChatViewModel.WorkspaceItem(id, "artifact", "Report.pdf", RunId: runId), Assert.Single(vm.ResultItems(result)));
        call["function"]!["name"] = "get_job";
        result["content"] = "not JSON";
        Assert.Empty(vm.ResultItems(result));
    }

    [Fact]
    public async Task Completed_workflow_opens_its_pdf_in_workspace_and_checks_artifact_access()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        using var scope = fixture.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var file = new RunArtifactLinkRow
        {
            Id = Guid.NewGuid(), ProjectId = fixture.ProjectId, JobId = Guid.NewGuid(), RunId = Guid.NewGuid(),
            Kind = "RawBundle", Title = "Report.pdf", ContentType = "application/pdf", CreatedAt = DateTimeOffset.UtcNow
        };
        db.RunArtifacts.Add(file);
        await db.SaveChangesAsync();
        var step = new ChainStepRunView(0, 0, 0, file.JobId, "Create report", file.RunId, "Succeeded", file.CreatedAt, file.CreatedAt);
        var run = new ChainRunView(Guid.NewGuid(), Guid.NewGuid(), "Report", "Succeeded", [step], "{}", file.CreatedAt, file.CreatedAt) { ProjectId = fixture.ProjectId };
        var dispatcher = new ToolDispatcher(fixture.ProjectId)
        {
            Run = run,
            Artifacts = [new(file.Id, file.RunId, PostJobActionKind.RawBundle, file.Title, file.ContentType, 100, file.CreatedAt)]
        };
        var reader = new ArtifactReader(new EfRunArtifactLinkRepository(db), null!);
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, null!, new(), new NoJs(), new(dispatcher, null!, null!, null!), fixture.Permissions, artifacts: reader);
        await vm.LoadAsync(fixture.ProjectId, runId: run.Id);
        Assert.Equal(file.Id, vm.PreviewArtifact?.Id);
        Assert.Equal(run, vm.PreviewRun);
        Assert.Null(vm.ArtifactPreviewError);
        var services = new ServiceCollection().AddLogging().AddSingleton(vm).AddSingleton<IJSRuntime, NoJs>().AddTransient<RunOutputViewModel>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<PlaceContext.Host.Components.Pages.BuildChat>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["ProjectId"] = fixture.ProjectId, ["RunId"] = run.Id }))).ToHtmlString());
        Assert.Contains($"data-pdf-url=\"{ArtifactsViewModel.Url(vm.PreviewArtifact!)}\"", html);
        Assert.Contains("Back to run", html);
        Assert.Contains("data-pc-pdf", html);
        vm.CloseArtifactPreview();
        await vm.RefreshPreviewRunAsync();
        Assert.Null(vm.PreviewArtifact);
        Assert.Equal(run, vm.PreviewRun);
        await vm.OpenArtifactAsync(file.Id, Guid.NewGuid());
        Assert.Null(vm.PreviewArtifact);
        Assert.NotNull(vm.ArtifactPreviewError);
        fixture.Permissions.Denied = Permission.ArtifactsView;
        await vm.OpenArtifactAsync(file.Id, file.RunId);
        Assert.Null(vm.PreviewArtifact);
        Assert.NotNull(vm.ArtifactPreviewError);
        fixture.Permissions.Denied = null;
        file.ProjectId = Guid.NewGuid();
        await db.SaveChangesAsync();
        await vm.OpenArtifactAsync(file.Id, file.RunId);
        Assert.Null(vm.PreviewArtifact);
        Assert.NotNull(vm.ArtifactPreviewError);
        await vm.DisposePageAsync();
    }

    [Fact]
    public async Task Json_artifact_preview_renders_monaco_json_instead_of_an_iframe()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        using var scope = fixture.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var file = new RunArtifactLinkRow
        {
            Id = Guid.NewGuid(), ProjectId = fixture.ProjectId, JobId = Guid.NewGuid(), RunId = Guid.NewGuid(),
            Kind = "RawBundle", Title = "Report.json", ContentType = "application/json",
            Bucket = "reports", ObjectKey = "runs/report.json", CreatedAt = DateTimeOffset.UtcNow
        };
        db.RunArtifacts.Add(file);
        await db.SaveChangesAsync();
        var reader = new ArtifactReader(new EfRunArtifactLinkRepository(db), new JsonStore(file.Bucket, file.ObjectKey, """{"summary":"ok","rows":[1,2]}"""));
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, null!, new(), new NoJs(), new(new ToolDispatcher(fixture.ProjectId), null!, null!, null!), fixture.Permissions, artifacts: reader);
        await vm.LoadAsync(fixture.ProjectId);
        await vm.OpenArtifactAsync(file.Id, file.RunId);
        Assert.Equal(file.Id, vm.PreviewArtifact?.Id);
        Assert.Null(vm.ArtifactPreviewError);
        Assert.Null(vm.PreviewArtifactJsonError);
        Assert.False(vm.PreviewArtifactJsonTruncated);
        Assert.Contains("\"summary\": \"ok\"", vm.PreviewArtifactJson);
        Assert.Contains("\n", vm.PreviewArtifactJson);
        var outputs = new List<RunOutputViewModel>();
        var services = new ServiceCollection().AddLogging().AddSingleton(vm).AddSingleton<IJSRuntime, NoJs>().AddTransient(_ =>
        {
            var output = new RunOutputViewModel(new NoJs());
            outputs.Add(output);
            return output;
        });
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<PlaceContext.Host.Components.Pages.BuildChat>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["ProjectId"] = fixture.ProjectId }))).ToHtmlString());
        Assert.DoesNotContain("<iframe", html);
        Assert.Contains("aria-label=\"Artifact JSON\"", html);
        var output = Assert.Single(outputs, x => x.Label == "Artifact JSON");
        Assert.Equal("json", output.Language);
        Assert.Contains("\"summary\": \"ok\"", output.Value);
        await vm.DisposePageAsync();
    }

    [Fact]
    public async Task Artifact_tool_requires_permission_and_returns_project_files()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var file = new ArtifactFileView(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), fixture.ProjectId, "RawBundle", "Report.pdf", "application/pdf", 100, DateTimeOffset.UtcNow);
        var dispatcher = new ToolDispatcher(fixture.ProjectId) { ProjectArtifacts = [file] };
        var tools = new BuildTools(fixture.ProjectId, new(dispatcher, null!, null!, null!), fixture.Permissions, fixture.Store);
        Assert.Equal(file, Assert.Single(await tools.list_artifacts()));
        fixture.Permissions.Denied = Permission.ArtifactsView;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.list_artifacts());
    }

    [Fact]
    public async Task Contextual_canvas_uses_saved_workflow_and_respects_permissions_and_project_boundaries()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var workflow = new JobChainView(Guid.NewGuid(), fixture.ProjectId, "Review", null,
            [new([new(Guid.NewGuid(), "Inspect input")])], DateTimeOffset.UtcNow);
        var dispatcher = new ToolDispatcher(fixture.ProjectId) { Workflow = workflow };
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, null!, new PortalUiState(), new NoJs(),
            new PlaceContextService(dispatcher, null!, null!, null!), fixture.Permissions);
        await vm.LoadAsync(fixture.ProjectId);
        vm.Draft = "Keep my question";
        await vm.OpenPreviewAsync(new(workflow.Id, "workflow", "Untrusted title"));
        Assert.Equal(workflow, vm.PreviewWorkflow);
        Assert.Equal("Review", vm.PreviewItem!.Name);
        Assert.Equal("Keep my question", vm.Draft);
        var services = new ServiceCollection().AddLogging().AddSingleton(vm).AddSingleton<IJSRuntime, NoJs>().AddTransient<RunOutputViewModel>().AddTransient<ChainCanvasViewModel>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<PlaceContext.Host.Components.Pages.BuildChat>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["ProjectId"] = fixture.ProjectId }));
            return rendered.ToHtmlString();
        });
        Assert.Contains("preview-canvas", html);
        Assert.Contains("Inspect input", html);
        Assert.DoesNotContain("workspace-tabs", html);
        Assert.DoesNotContain("<iframe", html);
        Assert.DoesNotContain("Remove stage", html);
        Assert.DoesNotContain("Add parallel job", html);
        fixture.Permissions.Allowed = false;
        await vm.OpenPreviewAsync(new(workflow.Id, "workflow", "Review"));
        Assert.Null(vm.PreviewWorkflow);
        Assert.NotNull(vm.PreviewError);
        fixture.Permissions.Allowed = true;
        dispatcher.Workflow = workflow with
        {
            ProjectId = Guid.NewGuid()
        };
        await vm.OpenPreviewAsync(new(workflow.Id, "workflow", "Review"));
        Assert.Null(vm.PreviewWorkflow);
        Assert.NotNull(vm.PreviewError);
        await vm.OpenJobPreviewAsync(Guid.NewGuid());
        Assert.Null(vm.PreviewJob);
        Assert.NotNull(vm.PreviewError);
    }

    [Fact]
    public async Task Home_renders_tiles_for_the_selected_project()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var ui = new PortalUiState();
        ui.SetProject(fixture.ProjectId, "Test project");
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, null!, ui, new NoJs(), fixture.Service, fixture.Permissions);
        var services = new ServiceCollection().AddLogging().AddSingleton(vm).AddSingleton(ui).AddSingleton<IJSRuntime, NoJs>().AddTransient<RunOutputViewModel>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<PlaceContext.Host.Components.Pages.Dashboard>()).ToHtmlString());
        Assert.DoesNotContain("Choose a task", html);
        Assert.Contains("class=\"chat-entry\" hidden", html);
        Assert.DoesNotContain("aria-label=\"Runs\"", html);
        Assert.DoesNotContain("aria-label=\"Charts\"", html);
        Assert.DoesNotContain("aria-label=\"Data\"", html);
        Assert.Equal(fixture.ProjectId, vm.Conversation.ProjectId);
        Assert.DoesNotContain("Workflow overview", html);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Opening_history_shows_loader_immediately_blocks_duplicate_clicks_and_recovers(bool fail)
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var store = DispatchProxy.Create<IBuildChatStore, DelayedChatStore>();
        var proxy = (DelayedChatStore)store;
        proxy.Inner = fixture.Store;
        var vm = new BuildChatViewModel(store, fixture.Connections, null!, new PortalUiState(), new NoJs(), fixture.Service, fixture.Permissions);
        var services = new ServiceCollection().AddLogging().AddSingleton(vm).AddSingleton<IJSRuntime, NoJs>().AddTransient<RunOutputViewModel>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<PlaceContext.Host.Components.Pages.BuildChat>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["ProjectId"] = fixture.ProjectId }));
            vm.Draft = "Keep this on error";
            vm.ShowHistory = true;
            var before = vm.Conversation;
            proxy.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var id = Guid.NewGuid();
            var opening = vm.OpenAsync(id);
            Assert.False(opening.IsCompleted);
            Assert.True(vm.Loading);
            Assert.True(vm.NavigationLocked);
            Assert.False(vm.ShowHistory);
            Assert.Contains("Loading session", rendered.ToHtmlString());
            Assert.Contains("aria-busy=\"true\"", rendered.ToHtmlString());
            await vm.OpenAsync(Guid.NewGuid());
            await vm.NewAsync();
            Assert.Equal(1, proxy.Loads);
            void AssertCompletion()
            {
                if (fail)
                {
                    proxy.Pending.SetException(new InvalidOperationException("History unavailable"));
                    return;
                }
                proxy.Pending.SetResult(new()
                {
                    Id = id,
                    ProjectId = fixture.ProjectId,
                    Title = "Saved"
                });
            }
            AssertCompletion();
            await opening;
            Assert.False(vm.Loading);
            Assert.False(vm.NavigationLocked);
            Assert.DoesNotContain("Loading session", rendered.ToHtmlString());
            void AssertCompletionCore()
            {
                if (fail)
                {
                    Assert.Same(before, vm.Conversation);
                    Assert.Equal("Keep this on error", vm.Draft);
                    Assert.Equal("History unavailable", vm.Error);
                    return;
                }
                Assert.Equal(id, vm.Conversation.Id);
                Assert.Empty(vm.Draft);
            }
            AssertCompletionCore();
        });
    }

    public class DelayedChatStore : DispatchProxy
    {
        public IBuildChatStore Inner = null!;
        public TaskCompletionSource<BuildConversation>? Pending;
        public TaskCompletionSource<IReadOnlyList<BuildConversationSummary>>? PendingHistory;
        public int MemoryLoads;
        public int Loads;
        public int TotalLoads;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(IBuildChatStore.ListAsync) && PendingHistory is not null)
            {
                return PendingHistory.Task;
            }
            if (method.Name == nameof(IBuildChatStore.GetMemoryAsync))
            {
                MemoryLoads++;
            }
            if (method!.Name == nameof(IBuildChatStore.LoadAsync))
            {
                TotalLoads++;
            }
            if (method.Name == nameof(IBuildChatStore.LoadAsync) && Pending is not null)
            {
                Loads++;
                return Pending.Task;
            }
            return method.Invoke(Inner, args);
        }
    }

    [Fact]
    public async Task Chart_tool_validates_data_and_history_reopens_an_accessible_chart_panel()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var tools = new BuildTools(fixture.ProjectId, fixture.Service, fixture.Permissions, fixture.Store);
        var chart = new ChartSpec("bar", "Outcomes", ["<script>bad</script>", "Failed"], [new ChartSeries("Runs", [12, 2])]);
        var output = await tools.show_chart(chart, "User supplied example values", default);
        await Assert.ThrowsAsync<ArgumentException>(() => tools.show_chart(chart with { Series = [new("Runs", [1])] }, "source", default));
        await Assert.ThrowsAsync<ArgumentException>(() => tools.show_chart(chart with { Series = [new("Runs", [double.NaN, 2])] }, "source", default));
        await Assert.ThrowsAsync<ArgumentException>(() => tools.show_chart(chart with { Type = "script" }, "source", default));
        await Assert.ThrowsAsync<ArgumentException>(() => tools.show_chart(chart, "", default));
        fixture.Permissions.Allowed = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.show_chart(chart, "source", default));
        fixture.Permissions.Allowed = true;
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, null!, new PortalUiState(), new NoJs(), fixture.Service, fixture.Permissions);
        await vm.LoadAsync(fixture.ProjectId);
        vm.Conversation.Messages.Add(new JsonObject
        {
            ["role"] = "assistant",
            ["tool_calls"] = new JsonArray(new JsonObject
            {
                ["id"] = "chart-call",
                ["function"] = new JsonObject { ["name"] = "show_chart", ["arguments"] = "{}" }
            })
        });
        var message = new JsonObject { ["role"] = "tool", ["tool_call_id"] = "chart-call", ["content"] = JsonSerializer.Serialize(output) };
        vm.Conversation.Messages.Add(message);
        var preview = Assert.IsType<BuildChatViewModel.ChartPreview>(vm.ResultChart(message));
        vm.OpenChart(preview);
        Assert.True(vm.WorkspaceOpen);
        var services = new ServiceCollection().AddLogging().AddSingleton(vm).AddSingleton<IJSRuntime, NoJs>().AddTransient<RunOutputViewModel>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<PlaceContext.Host.Components.Pages.BuildChat>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["ProjectId"] = fixture.ProjectId }))).ToHtmlString());
        Assert.Contains("View chart data", html);
        Assert.Matches("<figcaption[^>]*>User supplied example values</figcaption>", html);
        Assert.Contains("&lt;script&gt;bad&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>", html);
        vm.CloseWorkspace();
        Assert.False(vm.WorkspaceOpen);
        Assert.False(vm.WorkspaceVisible);
        Assert.Null(vm.PreviewChart);
        vm.OpenChart(preview);
        Assert.True(vm.WorkspaceVisible);
        vm.CloseWorkspace();
        vm.OpenWorkspace();
        Assert.True(vm.WorkspaceVisible);
        message["content"] = "{bad";
        Assert.Null(vm.ResultChart(message));
        message["content"] = JsonSerializer.Serialize(output);
        message["tool_call_id"] = "unknown";
        Assert.Null(vm.ResultChart(message));
    }

    [Fact]
    public async Task Conversation_preferences_can_be_saved_by_the_agent_and_used_in_another_chat()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var service = new PlaceContextService(new ToolDispatcher(fixture.ProjectId), null!, null!, null!);
        var model = new ScriptedModel("""{"tool_calls":[{"id":"memory","function":{"name":"remember","arguments":"{\"memory\":\"Prefer Python for data jobs.\"}"}}]}""");
        var agent = new BuildChatAgent(fixture.Store, model, service, fixture.Permissions, fixture.Tenant);
        var conversation = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test" };
        await agent.QueueAsync(conversation, "I prefer Python for data jobs.");
        await agent.RunAsync(conversation, default);
        Assert.Equal(BuildRunStatus.Completed, conversation.Status);
        Assert.Equal("Prefer Python for data jobs.", await fixture.Store.GetMemoryAsync(fixture.ProjectId));

        var nextModel = new ScriptedModel("""{"content":"I will use your Python preference."}""");
        var nextAgent = new BuildChatAgent(fixture.Store, nextModel, service, fixture.Permissions, fixture.Tenant);
        var next = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test" };
        await nextAgent.QueueAsync(next, "Help me plan a data job.");
        await nextAgent.RunAsync(next, default);
        Assert.Equal(BuildRunStatus.Completed, next.Status);
        Assert.Contains("Prefer Python for data jobs.", nextModel.SystemPrompt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task At_mentions_are_plain_text_and_legacy_mcp_selection_is_ignored(bool legacySelection)
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var service = new PlaceContextService(new ToolDispatcher(fixture.ProjectId), null!, null!, null!);
        var model = new PlainTextModel();
        var agent = new BuildChatAgent(fixture.Store, model, service, fixture.Permissions, fixture.Tenant);
        var conversation = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test" };
        const string text = "@Warehouse ask @\"Commerce MCP\" about user@example.test";
        await agent.QueueAsync(conversation, text);
        Assert.Null(conversation.Messages[0]!["mcp_connections"]);
        if (legacySelection)
        {
            conversation.Messages[0]!["mcp_connections"] = new JsonArray(Guid.NewGuid().ToString());
        }
        await agent.RunAsync(conversation, default);
        Assert.Equal(BuildRunStatus.Completed, conversation.Status);
        Assert.Equal(text, model.UserText);
        Assert.Equal("Done.", conversation.Messages.Last()!["content"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Mcp_error_content_is_not_reported_as_a_successful_tool_call(bool isError)
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        using var scope = fixture.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var server = new McpConnectionRow
        {
            Id = Guid.NewGuid(),
            ProjectId = fixture.ProjectId,
            Name = "Test",
            Transport = "http",
            EndpointUrl = "https://mcp.test",
            AuthType = "none",
            Enabled = true
        };
        db.McpConnections.Add(server);
        await db.SaveChangesAsync();
        fixture.Http.PostResponse = JsonSerializer.Serialize(new
        {
            result = new
            {
                isError,
                content = new[] { new { type = "text", text = "Tool result" } }
            }
        });
        var client = new McpClientService(new EfMcpConnectionRepository(db), null!, fixture.Http, null!, NullLogger<McpClientService>.Instance);
        var result = await client.CallToolAsync(server.Id, "lookup");
        Assert.Equal(!isError, result.Success);
        Assert.Contains("Tool result", isError ? result.Error : result.Content);
    }

    private sealed class PlainTextModel : IBuildModelGateway
    {
        public string? UserText;
        public Task<JsonObject> CompleteAsync(Guid connectionId, string model, JsonArray messages, JsonArray tools, CancellationToken ct, Action<string>? report = null)
        {
            if (messages[0]!["content"]!.ToString().StartsWith("You are the parent agent responsible"))
                return Task.FromResult(new JsonObject { ["content"] = "{\"approved\":true,\"reason\":\"Within scope\"}" });
            Assert.All(messages.OfType<JsonObject>(), message => Assert.False(message.ContainsKey("mcp_connections")));
            Assert.DoesNotContain(tools, tool => tool?["function"]?["name"]?.ToString() is "list_mcp_tools" or "call_mcp_tool");
            UserText = messages.Last()!["content"]![0]!["text"]!.GetValue<string>();
            return Task.FromResult(new JsonObject { ["content"] = "Done." });
        }
    }

    [Fact]
    public async Task Chat_submits_workflows_durably_and_returns_a_reopenable_run_without_waiting_for_execution()
    {
        var projectId = Guid.NewGuid();
        var workflow = new JobChainView(Guid.NewGuid(), projectId, "Review", null, [], DateTimeOffset.UtcNow);
        var dispatcher = new ToolDispatcher(projectId) { Workflow = workflow };
        var permissions = new Permissions();
        var queue = new SubmissionQueue(projectId, workflow.Id);
        var tools = new BuildTools(projectId, new(dispatcher, null!, null!, null!), permissions, null!, submissions: queue);
        var result = await tools.run_workflow(workflow.Id, "{\"input\":1}", default);
        Assert.Equal(1, queue.Submitted);
        Assert.Equal("{\"input\":1}", queue.Payload);
        Assert.Equal(0, dispatcher.Commands);
        var json = JsonSerializer.SerializeToNode(result)!;
        Assert.Equal(queue.Receipt.ChainRunId, json["id"]!.GetValue<Guid>());
        Assert.Equal("Queued", json["status"]!.GetValue<string>());
        permissions.Denied = Permission.JobsRun;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.run_workflow(workflow.Id, null, default));
        permissions.Denied = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => tools.run_workflow(Guid.NewGuid(), null, default));
        Assert.Equal(1, queue.Submitted);
    }

    [Fact]
    public async Task Chat_history_opens_a_queued_run_and_updates_steps_without_leaving_the_conversation()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var workflow = new JobChainView(Guid.NewGuid(), fixture.ProjectId, "Review", null, [], DateTimeOffset.UtcNow);
        var queue = new SubmissionQueue(fixture.ProjectId, workflow.Id);
        var dispatcher = new ToolDispatcher(fixture.ProjectId) { Workflow = workflow };
        var chat = new BuildConversation { ProjectId = fixture.ProjectId };
        var lease = await fixture.Store.AcquireAsync(chat);
        chat.Messages.Add(new JsonObject { ["role"] = "assistant", ["tool_calls"] = new JsonArray(new JsonObject
            { ["id"] = "run-1", ["function"] = new JsonObject { ["name"] = "run_workflow" } }) });
        chat.Messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = "run-1", ["content"] = JsonSerializer.Serialize(new
            { id = queue.Receipt.ChainRunId, chainId = workflow.Id, name = "Untrusted name", trackingId = queue.Receipt.TrackingId }) });
        await fixture.Store.SaveAsync(chat, lease, true);
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, null!, new(), new NoJs(),
            new(dispatcher, null!, null!, null!), fixture.Permissions, submissions: queue);
        await vm.LoadAsync(fixture.ProjectId, chat.Id);
        Assert.True(vm.WorkspaceVisible);
        Assert.Equal("Queued", vm.PreviewRunStatus);
        Assert.Equal("Review", vm.PreviewItem!.Name);
        Assert.Null(vm.PreviewRun);
        dispatcher.Run = new(queue.Receipt.ChainRunId, workflow.Id, workflow.Name, "Succeeded",
            [new(0, 0, 0, Guid.Empty, "First step", null, "Succeeded", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Output: "{\"first_result\":1}"),
             new(1, 1, 0, Guid.Empty, "Second step", null, "Succeeded", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Output: "{\"second_result\":2}")],
            "{\"combined\":true}", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow) { ProjectId = fixture.ProjectId };
        await vm.RefreshPreviewRunAsync();
        Assert.Equal("Succeeded", vm.PreviewRunStatus);
        Assert.Equal(chat.Id, vm.Conversation.Id);
        Assert.EndsWith($"/workflows/{workflow.Id}/runs/{dispatcher.Run.Id}", vm.PreviewEditorUrl);
        await vm.SelectPreviewStepAsync(dispatcher.Run.Steps[0]);
        Assert.Equal("{\"first_result\":1}", vm.PreviewStep!.Output);
        var outputs = new List<RunOutputViewModel>();
        var services = new ServiceCollection().AddLogging().AddSingleton(vm).AddSingleton<IJSRuntime, NoJs>().AddTransient(_ =>
        {
            var output = new RunOutputViewModel(new NoJs());
            outputs.Add(output);
            return output;
        });
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<PlaceContext.Host.Components.Pages.BuildChat>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { ["ProjectId"] = fixture.ProjectId, ["ConversationId"] = chat.Id }))).ToHtmlString());
        Assert.Contains("Workflow run steps", html);
        Assert.Contains("aria-label=\"Step output\"", html);
        Assert.Contains("first_result", Assert.Single(outputs, x => x.Label == "Step output").Value);
        Assert.DoesNotContain("second_result", Assert.Single(outputs, x => x.Label == "Step output").Value);
        Assert.Contains("Open full run", html);
        Assert.DoesNotContain("<iframe", html);
        await vm.SelectPreviewStepAsync(dispatcher.Run.Steps[1]);
        Assert.Equal("{\"second_result\":2}", vm.PreviewStep!.Output);
        vm.CloseWorkspace();
        Assert.Null(vm.PreviewRun);
        Assert.False(vm.WorkspaceVisible);
        await vm.DisposePageAsync();
    }

    [Fact]
    public async Task Run_preview_rechecks_permissions_ownership_and_ignores_a_response_after_close()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var run = new ChainRunView(Guid.NewGuid(), Guid.NewGuid(), "Run", "Succeeded", [], null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
            { ProjectId = fixture.ProjectId };
        var dispatcher = new ToolDispatcher(fixture.ProjectId) { Run = run };
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, null!, new(), new NoJs(),
            new(dispatcher, null!, null!, null!), fixture.Permissions);
        await vm.LoadAsync(fixture.ProjectId);
        var item = new BuildChatViewModel.WorkspaceItem(run.Id, "workflow-run", "Run", run.ChainId);
        await vm.OpenPreviewAsync(item);
        Assert.NotNull(vm.PreviewRun);
        fixture.Permissions.Denied = Permission.ChainsManage;
        await vm.RefreshPreviewRunAsync();
        Assert.Null(vm.PreviewRun);
        Assert.NotNull(vm.PreviewError);
        fixture.Permissions.Denied = null;
        dispatcher.Run = run with { ProjectId = Guid.NewGuid() };
        await vm.OpenPreviewAsync(item);
        Assert.Null(vm.PreviewRun);
        Assert.NotNull(vm.PreviewError);
        dispatcher.Run = run;
        dispatcher.PendingRun = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = vm.OpenPreviewAsync(item);
        vm.CloseWorkspace();
        dispatcher.PendingRun.SetResult(run);
        await pending;
        Assert.Null(vm.PreviewRun);
        Assert.Null(vm.PreviewItem);
        Assert.False(vm.WorkspaceVisible);
        await vm.DisposePageAsync();
    }

    [Fact]
    public async Task Step_logs_are_scoped_to_the_selected_run_and_project()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var step = new ChainStepRunView(0, 0, 0, Guid.NewGuid(), "Job", Guid.NewGuid(), "Succeeded", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var run = new ChainRunView(Guid.NewGuid(), Guid.NewGuid(), "Run", "Succeeded", [step], null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
            { ProjectId = fixture.ProjectId };
        var detail = new JobRunDetailView(step.RunId!.Value, step.JobId, fixture.ProjectId, "Succeeded", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            [new(0, 0, "Succeeded", "{\"real_output\":true}", "job log", [])], null, null!);
        var dispatcher = new ToolDispatcher(fixture.ProjectId) { Run = run, JobRun = detail };
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, null!, new(), new NoJs(),
            new(dispatcher, null!, null!, null!), fixture.Permissions);
        await vm.LoadAsync(fixture.ProjectId);
        await vm.OpenPreviewAsync(new(run.Id, "workflow-run", "Run", run.ChainId));
        Assert.Equal(detail, vm.PreviewStepRun);
        dispatcher.JobRun = detail with { ProjectId = Guid.NewGuid() };
        await vm.SelectPreviewStepAsync(step);
        Assert.Null(vm.PreviewStepRun);
        Assert.NotNull(vm.PreviewStepError);
        dispatcher.JobRun = detail;
        fixture.Permissions.Denied = Permission.JobsView;
        await vm.SelectPreviewStepAsync(step);
        Assert.Null(vm.PreviewStepRun);
        Assert.NotNull(vm.PreviewStepError);
        await vm.DisposePageAsync();
    }

    private sealed class SubmissionQueue(Guid projectId, Guid workflowId) : IJobChainSubmissionQueue
    {
        public JobChainSubmission Receipt = new(Guid.NewGuid(), projectId, workflowId, Guid.NewGuid(), "Queued", 0, null, DateTimeOffset.UtcNow, null, null);
        public int Submitted;
        public string? Payload;
        public List<(Guid RunId, Guid RequestId, string Values)> Responses = [];
        public Action? OnResume;
        public Task<JobChainSubmission> EnqueueAsync(Guid projectId, Guid chainId, string? inputPayload, string? idempotencyKey = null,
            CancellationToken ct = default, Guid? queueId = null, int? messageTtlSeconds = null, int receiptRetentionDays = 30,
            int maxAttempts = 3, int retryDelaySeconds = 3, DateTimeOffset? availableAt = null,
            IReadOnlyDictionary<int, string>? stepPayloadOverrides = null, Guid? replayOriginalRunId = null, int? replayFromStepIndex = null)
        {
            Assert.Equal(Receipt.ProjectId, projectId);
            Assert.Equal(Receipt.ChainId, chainId);
            Submitted++;
            Payload = inputPayload;
            return Task.FromResult(Receipt);
        }
        public Task<JobChainSubmission?> GetAsync(Guid trackingId, CancellationToken ct = default) => Task.FromResult<JobChainSubmission?>(trackingId == Receipt.TrackingId ? Receipt : null);
        public Task<string> ResumeAsync(Guid chainRunId, Guid inputRequestId, string valuesJson, CancellationToken ct = default)
        {
            Responses.Add((chainRunId, inputRequestId, valuesJson));
            OnResume?.Invoke();
            return Task.FromResult("Waiting");
        }
    }

    private sealed class JsonStore(string bucket, string key, string json) : IObjectStore
    {
        public bool IsEnabled => true;
        public string ReportsBucket => bucket;
        public string DepsBucket => "placecontext-deps";
        public Task<ObjectDownload?> OpenReadAsync(string requestedBucket, string requestedKey, CancellationToken ct = default) =>
            Task.FromResult<ObjectDownload?>(requestedBucket == bucket && requestedKey == key
                ? new ObjectDownload(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)), "application/json")
                : null);
        public Task PutAsync(string bucket, string key, byte[] content, string contentType, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string bucket, string key, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct = default) => Task.FromResult(false);
        public Task EnsureBucketAsync(string bucket, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> PresignDownloadAsync(string bucket, string key, TimeSpan ttl, CancellationToken ct = default) => Task.FromResult("");
        public Task<string> PresignUploadAsync(string bucket, string key, TimeSpan ttl, CancellationToken ct = default) => Task.FromResult("");
    }

    private sealed class ToolDispatcher(Guid projectId) : IDispatcher
    {
        public Dictionary<string, int> Counts = new() { ["Running"] = 3 };
        public PlaceContext.Application.ProjectData.ProjectQueryResult Data = new(["status", "total"], [["Ready", "2"]], 0, false);
        public TaskCompletionSource<PlaceContext.Application.ProjectData.ProjectQueryResult>? PendingData;
        public int DataQueries;
        public Guid DataProject;
        public string? DataSql;
        public string? DataError;
        public int Commands;
        public ChainRunView? Run;
        public JobChainView? Workflow;
        public JobRunDetailView? JobRun;
        public IReadOnlyList<RunArtifactLinkView> Artifacts = [];
        public IReadOnlyList<ArtifactFileView> ProjectArtifacts = [];
        public TaskCompletionSource<ChainRunView?>? PendingRun;
        public TaskCompletionSource<IReadOnlyList<JobChainView>>? PendingWorkflows;
        public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default)
        {
            Commands++;
            throw new InvalidOperationException();
        }
        public async Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default)
        {
            if (query is QueryProjectDataQuery dataQuery)
            {
                ++DataQueries;
                DataProject = dataQuery.ProjectId;
                DataSql = dataQuery.Sql;
                if (DataError is not null) throw new InvalidOperationException(DataError);
                return (TResult)(object)(PendingData is null ? Data : await PendingData.Task);
            }
            if (query is ListJobChainsQuery && PendingWorkflows is not null)
            {
                return (TResult)(object)await PendingWorkflows.Task;
            }
            if (query is GetChainRunQuery && PendingRun is not null)
            {
                return (TResult)(object)(await PendingRun.Task)!;
            }
            object result = query switch
            {
                GetProjectByIdQuery => new ProjectSummaryView(projectId, "test", "/tmp", "Registered", false, 0, 0, 0),
                ListJobsQuery => Array.Empty<JobView>(),
                GetProjectRunCountsQuery => Counts,
                ListProjectTableSchemasQuery => Array.Empty<PlaceContext.Application.ProjectData.ProjectTableSchema>(),
                ListJobChainsQuery => Workflow is null ? Array.Empty<JobChainView>() : new[] { Workflow },
                ListTriggersQuery => Array.Empty<TriggerView>(),
                ListChainRunsQuery => Array.Empty<ChainRunView>(),
                GetChainRunQuery => Run!,
                GetJobRunQuery => JobRun!,
                ListRunArtifactsQuery artifactQuery => Artifacts.Where(a => a.RunId == artifactQuery.RunId).ToArray(),
                ListProjectArtifactsQuery => ProjectArtifacts,
                GetJobQuery => JsonSerializer.Deserialize<JobView>($$"""{"Id":"{{Guid.NewGuid()}}","ProjectId":"{{Guid.NewGuid()}}"}""")!,
                _ => throw new InvalidOperationException(),
            };
            return (TResult)result;
        }
    }

    [Fact]
    public async Task Jev_reviews_only_completed_evidence_from_the_current_project_after_permission_checks()
    {
        var project = Guid.NewGuid();
        var permissions = new Permissions();
        var dispatcher = new ToolDispatcher(project);
        dispatcher.Run = new(Guid.NewGuid(), Guid.NewGuid(), "Compare", "Succeeded", [], "Stored price evidence", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        {
            ProjectId = project
        };
        var reviewer = new Reviewer();
        var tools = new BuildTools(project, new PlaceContextService(dispatcher, null!, null!, null!), permissions, null!, reviewer);
        var result = (JsonObject)await tools.review_workflow_result(dispatcher.Run.Id, "Are both prices verified?", default);
        Assert.Equal(.4, result["noul"]!.GetValue<double>());
        Assert.Contains("Stored price evidence", reviewer.State);
        Assert.Contains("Succeeded", reviewer.State);
        Assert.Contains("untrusted evidence", reviewer.Question);
        permissions.Allowed = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.review_workflow_result(dispatcher.Run.Id, "Check", default));
        permissions.Allowed = true;
        dispatcher.Run = dispatcher.Run with
        {
            ProjectId = Guid.NewGuid()
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => tools.review_workflow_result(dispatcher.Run.Id, "Check", default));
        dispatcher.Run = dispatcher.Run with
        {
            ProjectId = project,
            FinishedAt = null
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => tools.review_workflow_result(dispatcher.Run.Id, "Check", default));
        Assert.Equal(1, reviewer.Calls);
        Assert.Equal(0, dispatcher.Commands);
    }

    private sealed class Reviewer : IOutcomeReviewer
    {
        public int Calls;
        public double Confidence = .4;
        public bool Fail, WaitForCancellation;
        public Guid? ProjectId;
        public string? State, Question;
        public Task<JsonObject> ScoreAsync(IReadOnlyList<PlaceContext.Domain.ValueObjects.ScoreCriterion> criteria, string state, CancellationToken ct, Guid? projectId = null) => throw new NotSupportedException();
        public async Task<JsonObject> EvaluateAsync(string question, string? state, CancellationToken ct, Guid? projectId = null)
        {
            Calls++;
            State = state;
            Question = question;
            ProjectId = projectId;
            if (Fail)
            {
                throw new InvalidOperationException("Review service unavailable");
            }
            if (WaitForCancellation)
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            return new JsonObject { ["type"] = "noul", ["noul"] = Confidence, ["model"] = "jev-test" };
        }
    }
    private sealed class User : ICurrentUser
    {
        public Guid UserId { get; set; } = Guid.NewGuid(); public string Role => "Owner"; public bool IsAuthenticated => true;
    }
    private sealed class Tenant : ICurrentTenant
    {
        public Guid TenantId { get; set; } = Guid.NewGuid(); public string Slug { get; set; } = "test"; public string TimeZoneId => "UTC"; public bool IsResolved => true;
    }
    private sealed class Permissions : IPermissionService
    {
        public bool Allowed = true;
        public string? Denied;
        public Task<bool> HasAsync(string permission, CancellationToken ct = default) => Task.FromResult(Allowed && permission != Denied);
        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlySet<string>>(Allowed ? Permission.All.ToHashSet() : new HashSet<string>());
        public Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(Guid userId, string roleName, CancellationToken ct = default) => GetEffectivePermissionsAsync(ct);
    }
    public class MembersProxy : DispatchProxy
    {
        public bool Allowed = true;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name == "IsDefaultAdminAsync" ? Task.FromResult(Allowed) : throw new NotSupportedException();
    }
    private sealed class Http : HttpMessageHandler, IHttpClientFactory
    {
        public int Requests;
        public string? Authorization;
        public string ModelsResponse = """{"data":[]}""";
        public string PostResponse = """{"choices":[{"message":{"role":"assistant","content":"OK"}}]}""";
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(request.Method == HttpMethod.Get ? ModelsResponse : PostResponse) });
        }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public async Task<McpConnectionInfo> SeedMcpAsync(bool enabled = true, Guid? projectId = null)
        {
            var connection = new McpConnectionInfo(Guid.NewGuid(), "MCP test", "http", "https://mcp.test/?private-token=secret", enabled);
            using var scope = Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.McpConnections.Add(new()
            {
                Id = connection.Id, ProjectId = projectId ?? ProjectId, Name = connection.Name, Transport = "http",
                EndpointUrl = connection.EndpointUrl, AuthType = "none", Enabled = enabled
            });
            await db.SaveChangesAsync();
            return connection;
        }
        public readonly User User = new(); public readonly Tenant Tenant = new(); public readonly Permissions Permissions = new(); public readonly Http Http = new();
        public readonly OperationCenter Operations = new(null!, default);
        public readonly Guid ProjectId = Guid.NewGuid();
        public readonly Guid ConnectionId = Guid.NewGuid();
        public PlaceContextService Service => new(new ToolDispatcher(ProjectId), null!, null!, null!);
        public ServiceProvider Provider
        {
            get;
        }
        public DbBuildChatStore Store
        {
            get;
        }
        public ModelConnections Connections
        {
            get;
        }
        public IMembershipService Members { get; } = DispatchProxy.Create<IMembershipService, MembersProxy>();
        public Fixture(IBuildModelGateway? gateway = null)
        {
            var services = new ServiceCollection();
            var database = Guid.NewGuid().ToString();
            services.AddSingleton<ICurrentTenant>(Tenant);
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(database));
            var encryption = new DataProtectionEncryptor(new EphemeralDataProtectionProvider());
            services.AddSingleton<IDataEncryptor>(encryption);
            services.AddScoped<IBuildChatStore>(_ => Store!);
            services.AddScoped(_ => new BuildChatAgent(Store!, gateway ?? new ScriptedModel(), Service, Permissions, Tenant, operations: Operations, user: User));
            Provider = services.BuildServiceProvider();
            Store = new(Provider.GetRequiredService<IServiceScopeFactory>(), User, Tenant, Permissions, encryption);
            Connections = new(Provider.GetRequiredService<IServiceScopeFactory>(), User, Tenant, Permissions, Members, encryption, Http);
        }
        public async Task SeedAsync()
        {
            using var scope = Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Projects.Add(new()
            {
                Id = ProjectId,
                Name = "test",
                Path = "/tmp",
                Status = "Registered"
            });
            db.Tenants.Add(new()
            {
                Id = Tenant.TenantId,
                Name = "Test",
                Slug = "test"
            });
            db.Users.Add(new()
            {
                Id = User.UserId,
                Role = User.Role,
                Email = "test@example.test"
            });
            db.ModelConnections.Add(new()
            {
                Id = ConnectionId,
                Name = "Test",
                BaseUrl = "http://localhost:11434/v1",
                ModelsJson = "[\"test\"]"
            });
            await db.SaveChangesAsync();
        }
        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }
}
