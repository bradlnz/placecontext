using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.JSInterop;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Host;
using PlaceContext.Host.Components.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application;
using PlaceContext.Application.Build;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Build;
using PlaceContext.Infrastructure.Build;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Operations;
using PlaceContext.Infrastructure.Security;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Host.Tests;

public sealed class BuildChatTests
{
    [Fact]
    public void Anthropic_converts_images_tool_calls_and_multiple_results()
    {
        var messages = JsonNode.Parse("""
            [{"role":"system","content":"system"},
             {"role":"user","content":[{"type":"text","text":"look"},{"type":"image_url","image_url":{"url":"data:image/png;base64,AAAA"}}]},
             {"role":"assistant","content":null,"tool_calls":[{"id":"a","function":{"name":"list_jobs","arguments":"{}"}},{"id":"b","function":{"name":"list_schedules","arguments":"{}"}}]},
             {"role":"tool","tool_call_id":"a","content":"[]"},{"role":"tool","tool_call_id":"b","content":"[]"}]
            """)!.AsArray();
        var request = ModelWireFormat.Request("anthropic", "claude-test", messages, []);
        Assert.Equal("system", request["system"]!.GetValue<string>());
        Assert.Equal("image", request["messages"]![0]!["content"]![1]!["type"]!.GetValue<string>());
        Assert.Equal("image/png", request["messages"]![0]!["content"]![1]!["source"]!["media_type"]!.GetValue<string>());
        Assert.Equal(3, request["messages"]!.AsArray().Count);
        Assert.Equal(2, request["messages"]![2]!["content"]!.AsArray().Count);
        Assert.Equal("tool_result", request["messages"]![2]!["content"]![0]!["type"]!.GetValue<string>());
        var response = ModelWireFormat.Response("anthropic", JsonNode.Parse("""{"content":[{"type":"text","text":"Checking"},{"type":"tool_use","id":"c","name":"list_jobs","input":{}}]}""")!.AsObject());
        Assert.Equal("Checking", response["content"]!.GetValue<string>());
        Assert.Equal("{}", response["tool_calls"]![0]!["function"]!["arguments"]!.GetValue<string>());
    }

    [Fact]
    public void Incomplete_model_responses_cannot_execute_tools()
    {
        Assert.Throws<InvalidOperationException>(() => ModelWireFormat.Response("anthropic", new() { ["stop_reason"] = "max_tokens" }));
        Assert.Throws<InvalidOperationException>(() => ModelWireFormat.Response("openai", JsonNode.Parse("""{"choices":[{"finish_reason":"length"}]}""")!.AsObject()));
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
    public void Images_reject_mislabeled_content_and_oversize_uploads()
    {
        Assert.Throws<ArgumentException>(() => new BuildImage("x.png", "image/png", "<svg>not a png</svg>"u8.ToArray()).Validate());
        Assert.Throws<ArgumentException>(() => new BuildImage("x.png", "image/png", new byte[2_000_001]).Validate());
        new BuildImage("x.png", "image/png", [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0]).Validate();
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
        await fixture.Connections.SaveAsync(draft with { ApiKey = null });
        var reply = await fixture.Connections.CompleteAsync(id, "test", [], [], default);
        Assert.Equal("OK", reply["content"]!.GetValue<string>());
        Assert.Equal("Bearer secret-key", fixture.Http.Authorization);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Connections.CompleteAsync(id, "unconfigured", [], [], default));
        await fixture.Connections.SaveAsync(draft with { Enabled = false, ApiKey = null });
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
    public async Task Agent_executes_tool_round_trip_and_persists_history()
    {
        await using var fixture = new Fixture(); await fixture.SeedAsync();
        var gateway = new ScriptedModel();
        var operations = new OperationCenter(null!, null!);
        var agent = new BuildChatAgent(fixture.Store, gateway, fixture.Service, fixture.Permissions, fixture.Tenant,
            operations: operations, user: fixture.User);
        var conversation = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test" };
        await agent.QueueAsync(conversation, "How do jobs work?", []);
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
        await using var fixture = new Fixture(); await fixture.SeedAsync();
        var gateway = new ScriptedModel("""{"tool_calls":[{"id":"q","type":"function","function":{"name":"ask_user","arguments":"{\"question\":\"Which language?\",\"answers\":[\" Use C# \",\"Use Python\"]}"}}]}""");
        var agent = new BuildChatAgent(fixture.Store, gateway, fixture.Service, fixture.Permissions, fixture.Tenant);
        var conversation = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test" };
        await agent.QueueAsync(conversation, "Help me choose a language", []);
        await agent.RunAsync(conversation, default);
        Assert.Equal(1, gateway.Requests);
        Assert.Equal(BuildRunStatus.Completed, conversation.Status);
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, agent, new PortalUiState(), new NoJs());
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
        finally { await vm.DisposePageAsync(); }
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
        await using var fixture = new Fixture(); await fixture.SeedAsync();
        var gateway = new ScriptedModel("""{"tool_calls":[{"id":"q","function":{"name":"ask_user","arguments":"{\"question\":\"Proceed?\",\"answers\":[\"Yes\",\"No\"]}"}},{"id":"m","function":{"name":"remember","arguments":"{\"memory\":\"Must not be saved\"}"}}]}""");
        var agent = new BuildChatAgent(fixture.Store, gateway, fixture.Service, fixture.Permissions, fixture.Tenant);
        var conversation = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test" };
        await agent.QueueAsync(conversation, "Ask before proceeding", []);
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
        await using var fixture = new Fixture(); await fixture.SeedAsync();
        var agent = new BuildChatAgent(fixture.Store, new FailingModel(cancelled), fixture.Service, fixture.Permissions, fixture.Tenant);
        var conversation = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test" };
        await agent.QueueAsync(conversation, "Help", []);
        await agent.RunAsync(conversation, default);
        var saved = await fixture.Store.LoadAsync(fixture.ProjectId, conversation.Id);
        Assert.Equal("user", saved.Messages[0]!["role"]!.GetValue<string>());
        Assert.Equal(BuildRunStatus.Failed, saved.Status);
        if (cancelled) Assert.Contains("without a Build stop request", saved.Error);
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
            new QemuChainActionView("linux", "Inspect the form", "result", 2048, 2, 120, true));
        var modelInput = WorkflowStage.FromView(view);
        var json = JsonSerializer.Serialize(modelInput);
        var stage = JsonSerializer.Deserialize<WorkflowStage>(json)!.ToStage();
        var action = Assert.IsType<QemuChainAction>(stage.Action);
        Assert.Equal("Inspect the form", action.Command);
        Assert.Equal(2048, action.MemoryMb);
        var condition = Assert.IsType<ConditionGate>(stage.Gate);
        Assert.Equal(jobId, Assert.Single(Assert.Single(condition.ElseBranch!).JobIds));
        Assert.Equal(TimeSpan.FromSeconds(30), Assert.IsType<WaitGate>(condition.ElseBranch![0].Gate).Duration);
    }

    [Theory]
    [InlineData(null, "Build", null)]
    [InlineData("""{"workspace":[{"id":"build.chat","label":"Chat","order":10,"visible":true,"section":"Build"}]}""", "Build", null)]
    [InlineData("""{"workspace":[{"id":"build.chat","label":"Assistant","order":10,"visible":true,"section":"Custom"}]}""", "Assistant", "Custom")]
    public async Task Build_menu_is_a_single_link_and_preserves_custom_labels(string? menuJson, string label, string? section)
    {
        await using var fixture = new Fixture();
        using (var scope = fixture.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Tenants.Add(new() { Id = fixture.Tenant.TenantId, Name = "Test", Slug = "test", MenuJson = menuJson });
            await db.SaveChangesAsync();
        }
        var service = new MenuConfigService(fixture.Provider.GetRequiredService<IServiceScopeFactory>(),
            fixture.Tenant, fixture.Permissions, fixture.User);
        var items = await service.GetWorkspaceMenuAsync(fixture.ProjectId);
        var build = Assert.Single(items, x => x.Id == "build.chat");
        Assert.Equal(label, build.Label);
        Assert.Equal("link", build.Kind);
        Assert.Equal(section, build.Section);
        Assert.Equal($"/project/{fixture.ProjectId}/build", build.Href);
        if (section is null) Assert.Null(build.Parent);
        Assert.DoesNotContain(items, x => x.Kind == "group" && x.Label == "Build");
    }

    [Fact]
    public async Task Chat_renders_inputs_model_selection_and_escapes_assistant_HTML()
    {
        await using var fixture = new Fixture(); await fixture.SeedAsync();
        var connectionId = Guid.NewGuid();
        await fixture.Connections.SaveAsync(new(connectionId, "Test provider", "openai", "http://localhost:11434/v1", ["test-model"], true));
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, null!, new PortalUiState(), new NoJs());
        await vm.LoadAsync(fixture.ProjectId);
        vm.SelectConnection(connectionId.ToString());
        vm.Conversation.Messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = "<script>alert(1)</script>",
            ["suggested_answers"] = new JsonArray("<script>answer</script>", "Use Python") });
        var services = new ServiceCollection().AddLogging().AddSingleton(vm).AddSingleton<IJSRuntime, NoJs>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<PlaceContext.Host.Components.Pages.BuildChat>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["ProjectId"] = fixture.ProjectId }));
            return rendered.ToHtmlString();
        });
        Assert.Contains("Test provider", html);
        Assert.Contains("test-model", html);
        Assert.Contains("Attach images", html);
        Assert.Contains("Dictate", html);
        Assert.Contains("Project memory", html);
        Assert.DoesNotContain("<aside", html);
        Assert.Contains("aria-label=\"History\"", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("aria-label=\"Suggested answers\"", html);
        Assert.Contains("&lt;script&gt;answer&lt;/script&gt;</button>", html);
        Assert.Contains("Use Python</button>", html);
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
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, null!, new PortalUiState(), new NoJs());
        try
        {
            await vm.LoadAsync(fixture.ProjectId);
            Assert.Null(vm.Error);
            Assert.Equal(enabled && hasModels ? localId : remoteId, vm.Conversation.ConnectionId);
            Assert.Equal(enabled && hasModels ? "local-model" : "remote-model", vm.Conversation.Model);
            vm.SelectConnection(remoteId.ToString());
            await vm.NewAsync();
            Assert.Equal(remoteId, vm.Conversation.ConnectionId);
            Assert.Equal("remote-model", vm.Conversation.Model);
        }
        finally { await vm.DisposePageAsync(); }
    }

    [Fact]
    public async Task Background_run_survives_page_disposal_and_reconnects_to_persisted_progress()
    {
        var model = new WaitingModel();
        await using var fixture = new Fixture(model); await fixture.SeedAsync();
        var agent = fixture.Provider.GetRequiredService<BuildChatAgent>();
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, agent, new PortalUiState(), new NoJs());
        await vm.LoadAsync(fixture.ProjectId);
        vm.Draft = "Inspect this project.";
        await vm.SendAsync();
        var id = vm.Conversation.Id;
        Assert.True(vm.Busy);
        var worker = new BuildChatWorker(fixture.Provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<BuildChatWorker>.Instance);
        var run = worker.ProcessAsync(await CandidateAsync(fixture, id), default);
        await model.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await vm.DisposePageAsync();
        Assert.False(run.IsCompleted);
        var reopened = new BuildChatViewModel(fixture.Store, fixture.Connections, agent, new PortalUiState(), new NoJs());
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
        await using var fixture = new Fixture(model); await fixture.SeedAsync();
        var agent = fixture.Provider.GetRequiredService<BuildChatAgent>();
        var conversation = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test" };
        await agent.QueueAsync(conversation, "Help", []);
        var run = agent.RunAsync(conversation, default);
        await model.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var before = (await CandidateAsync(fixture, conversation.Id)).LeaseUntil;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        while ((await CandidateAsync(fixture, conversation.Id)).LeaseUntil == before)
            await Task.Delay(100, timeout.Token);
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
        await using var fixture = new Fixture(); await fixture.SeedAsync();
        var agent = fixture.Provider.GetRequiredService<BuildChatAgent>();
        var conversation = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test" };
        await agent.QueueAsync(conversation, "Help", []);
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
        await using var fixture = new Fixture(model); await fixture.SeedAsync();
        var agent = fixture.Provider.GetRequiredService<BuildChatAgent>();
        var conversation = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test" };
        await agent.QueueAsync(conversation, "Help", []);
        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.QueueAsync(conversation, "Duplicate", []));
        var worker = new BuildChatWorker(fixture.Provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<BuildChatWorker>.Instance);
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
        await using var fixture = new Fixture(model); await fixture.SeedAsync();
        var agent = fixture.Provider.GetRequiredService<BuildChatAgent>();
        var conversation = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test" };
        await agent.QueueAsync(conversation, "Help", []);
        var worker = new BuildChatWorker(fixture.Provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<BuildChatWorker>.Instance);
        var candidate = await CandidateAsync(fixture, conversation.Id);
        fixture.Permissions.Allowed = false;
        await worker.ProcessAsync(candidate, default);
        fixture.Permissions.Allowed = true;
        Assert.Equal(BuildRunStatus.Failed, (await fixture.Store.LoadAsync(fixture.ProjectId, conversation.Id)).Status);
        await agent.QueueAsync(conversation, "Try again", []);
        await fixture.Store.RequestStopAsync(fixture.ProjectId, conversation.Id);
        await worker.ProcessAsync(await CandidateAsync(fixture, conversation.Id), default);
        Assert.Equal(BuildRunStatus.Stopped, (await fixture.Store.LoadAsync(fixture.ProjectId, conversation.Id)).Status);
        Assert.Equal(0, model.Requests);
    }

    [Fact]
    public async Task Expired_background_claim_is_interrupted_without_reexecuting_tools()
    {
        await using var fixture = new Fixture(); await fixture.SeedAsync();
        var agent = fixture.Provider.GetRequiredService<BuildChatAgent>();
        var conversation = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test" };
        await agent.QueueAsync(conversation, "Help", []);
        var lease = await fixture.Store.AcquireAsync(conversation);
        conversation.Messages.Add(JsonNode.Parse("""{"role":"assistant","tool_calls":[{"id":"unknown-action","function":{"name":"run_job","arguments":"{}"}}]}"""));
        await fixture.Store.SaveAsync(conversation, lease, false);
        using (var scope = fixture.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.BuildConversations.SingleAsync()).LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        var worker = new BuildChatWorker(fixture.Provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<BuildChatWorker>.Instance);
        await worker.ProcessAsync(await CandidateAsync(fixture, conversation.Id), default);
        var saved = await fixture.Store.LoadAsync(fixture.ProjectId, conversation.Id);
        Assert.Equal(BuildRunStatus.Interrupted, saved.Status);
        Assert.Contains(saved.Messages, x => x?["role"]?.GetValue<string>() == "tool" && x["content"]!.GetValue<string>().Contains("unknown"));
    }

    [Fact]
    public async Task Local_discovery_detects_models_without_keys_and_preserves_manual_configuration()
    {
        await using var fixture = new Fixture();
        fixture.Http.ModelsResponse = """{"data":[{"id":"local-model"},{"id":"local-model"},{"id":""}]}""";
        var config = new ConfigurationBuilder().Build();
        LocalModelDiscovery Discovery(string environment = "Development") => new(
            fixture.Provider.GetRequiredService<IServiceScopeFactory>(), fixture.Tenant, fixture.Http,
            new TestEnvironment { EnvironmentName = environment }, config);
        await Discovery("Production").DetectAsync();
        Assert.Empty(await fixture.Connections.ListAsync());
        await Discovery().DetectAsync();
        var connections = await fixture.Connections.ListAsync();
        Assert.Equal(2, connections.Count);
        Assert.All(connections, c => { Assert.Equal(new[] { "local-model" }, c.Models); Assert.False(c.HasApiKey); });
        var manual = connections[0];
        await fixture.Connections.SaveAsync(new(manual.Id, "Custom local provider", "openai", manual.BaseUrl, ["manual-model"], false));
        fixture.Http.ModelsResponse = """{"data":[{"id":"new-model"}]}""";
        await Discovery().DetectAsync();
        connections = await fixture.Connections.ListAsync();
        Assert.Equal(2, connections.Count);
        var preserved = connections.Single(x => x.Id == manual.Id);
        Assert.False(preserved.Enabled);
        Assert.Equal(new[] { "manual-model" }, preserved.Models);
        Assert.Equal(new[] { "new-model" }, connections.Single(x => x.Id != manual.Id).Models);
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
        public async Task<JsonObject> CompleteAsync(Guid connectionId, string model, JsonArray messages, JsonArray tools, CancellationToken ct)
        {
            Requests++;
            Started.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return new() { ["role"] = "assistant", ["content"] = "Finished." };
        }
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "/tmp";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class NoJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }
    private sealed class FailingModel(bool cancelled) : IBuildModelGateway
    {
        public Task<JsonObject> CompleteAsync(Guid connectionId, string model, JsonArray messages, JsonArray tools, CancellationToken ct) =>
            throw (cancelled ? new OperationCanceledException(ct) : new InvalidOperationException("Provider unavailable."));
    }

    private sealed class ScriptedModel(string? firstReply = null) : IBuildModelGateway
    {
        public int Requests;
        public Task<JsonObject> CompleteAsync(Guid connectionId, string model, JsonArray messages, JsonArray tools, CancellationToken ct)
        {
            Requests++;
            Assert.All(messages.OfType<JsonObject>(), message => Assert.False(message.ContainsKey("suggested_answers")));
            if (Requests == 2) Assert.Contains(messages, x => x?["role"]?.GetValue<string>() == "tool");
            return Task.FromResult(JsonNode.Parse(Requests == 1
                ? firstReply ?? """{"role":"assistant","tool_calls":[{"id":"a","type":"function","function":{"name":"job_authoring_guide","arguments":"{}"}}]}"""
                : """{"role":"assistant","content":"Here is the contract."}""")!.AsObject());
        }
    }
    [Fact]
    public async Task Hosting_tools_deploy_source_and_an_owned_database_using_the_current_project()
    {
        await using var fixture = new Fixture();
        var hosting = new Hosting();
        var tools = new BuildTools(fixture.ProjectId, fixture.Service, fixture.Permissions, fixture.Store, hosting, hosting);
        var functions = tools.Functions().ToDictionary(function => function.Name);
        Assert.Contains("launch_service", functions.Keys);
        Assert.Contains("create_database", functions.Keys);
        var definition = new ServiceLaunch("Review app", "code", 8080, RequireSso: true,
            Code: new("python", "main.py", [new("main.py", "print('app')")]));
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["definition"] = definition });
        await functions["launch_service"].InvokeAsync(args);
        Assert.Equal(fixture.ProjectId, hosting.Project);
        Assert.Equal(definition, hosting.Launched);
        await tools.create_database(new("Review data", "postgres", ServiceId: hosting.Id), default);
        Assert.Equal(hosting.Id, hosting.Created!.ServiceId);
        await tools.update_service_code(hosting.Id, definition.Code!, default);
        Assert.Equal(definition.Code, hosting.Updated);
        Assert.Contains("DATABASE_URL", tools.service_authoring_guide());
        Assert.Contains("Services", JsonSerializer.Serialize(await tools.project_context(default)));
        fixture.Permissions.Allowed = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.launch_service(definition, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.create_database(new("Data", "redis", ServiceId: hosting.Id), default));
        Assert.Equal(1, hosting.LaunchCount);
    }

    [Fact]
    public async Task Deep_link_loads_the_requested_chat_and_new_chat_survives_parameter_refresh()
    {
        await using var fixture = new Fixture(); await fixture.SeedAsync();
        var chat = new BuildConversation { ProjectId = fixture.ProjectId, Title = "Earlier Build" };
        var lease = await fixture.Store.AcquireAsync(chat);
        await fixture.Store.SaveAsync(chat, lease, true);
        var ui = new PortalUiState(); ui.Set("Jobs", "existing page");
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, null!, ui, new NoJs());
        await vm.LoadAsync(fixture.ProjectId, chat.Id);
        Assert.Equal(chat.Id, vm.Conversation.Id);
        Assert.Equal("Build", ui.Title);
        await vm.NewAsync();
        var fresh = vm.Conversation.Id;
        await vm.LoadAsync(fixture.ProjectId, chat.Id);
        Assert.Equal(fresh, vm.Conversation.Id);
        await vm.DisposePageAsync();
    }

    private sealed class Hosting : IServiceConsole, IDatabaseConsole
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Guid Project;
        public ServiceLaunch? Launched;
        public ServiceCode? Updated;
        public DatabaseLaunch? Created;
        public int LaunchCount;
        public Task<Guid> LaunchAsync(Guid projectId, ServiceLaunch service, CancellationToken ct = default)
        { Project = projectId; Launched = service; LaunchCount++; return Task.FromResult(Id); }
        public Task<IReadOnlyList<ServiceInstance>> ListAsync(Guid projectId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ServiceInstance>>(Launched is null ? [] : [new(Id, Launched.Name, Launched.Image, "starting", null, null, DateTimeOffset.UtcNow, RequireSso: true)]);
        Task<IReadOnlyList<DatabaseInstance>> IDatabaseConsole.ListAsync(Guid projectId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DatabaseInstance>>(Created is null ? [] : [new(Guid.NewGuid(), Created.Name, Created.Engine, "starting", null, "private", 5432, "app", "app", "data", DateTimeOffset.UtcNow, Created.ServiceId)]);
        public Task CreateAsync(Guid projectId, DatabaseLaunch database, CancellationToken ct = default)
        { Project = projectId; Created = database; return Task.CompletedTask; }
        public Task<ServiceCode?> GetCodeAsync(Guid projectId, Guid serviceId, CancellationToken ct = default) => Task.FromResult(Launched?.Code);
        public Task UpdateCodeAsync(Guid projectId, Guid serviceId, ServiceCode code, CancellationToken ct = default)
        { Project = projectId; Updated = code; return Task.CompletedTask; }
        public Task ControlAsync(Guid projectId, Guid id, string action, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> LogsAsync(Guid projectId, Guid id, CancellationToken ct = default) => Task.FromResult("ready");
        public Task<Uri> GetSsoEndpointAsync(Guid projectId, Guid serviceId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<DatabaseQueryResult> QueryAsync(Guid projectId, Guid databaseId, string query, string? cursor = null, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class ToolDispatcher(Guid projectId) : IDispatcher
    {
        public int Commands;
        public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default) { Commands++; throw new InvalidOperationException(); }
        public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default)
        {
            object result = query switch
            {
                GetProjectByIdQuery => new ProjectSummaryView(projectId, "test", "/tmp", "Registered", false, 0, 0, 0),
                ListJobsQuery => Array.Empty<JobView>(),
                ListJobChainsQuery => Array.Empty<JobChainView>(),
                ListTriggersQuery => Array.Empty<TriggerView>(),
                GetJobQuery => JsonSerializer.Deserialize<JobView>($$"""{"Id":"{{Guid.NewGuid()}}","ProjectId":"{{Guid.NewGuid()}}"}""")!,
                _ => throw new InvalidOperationException(),
            };
            return Task.FromResult((TResult)result);
        }
    }
    private sealed class User : ICurrentUser { public Guid UserId { get; set; } = Guid.NewGuid(); public string Role => "Owner"; public bool IsAuthenticated => true; }
    private sealed class Tenant : ICurrentTenant { public Guid TenantId { get; set; } = Guid.NewGuid(); public string Slug { get; set; } = "test"; public string TimeZoneId => "UTC"; public bool IsResolved => true; }
    private sealed class Permissions : IPermissionService
    {
        public bool Allowed = true;
        public Task<bool> HasAsync(string permission, CancellationToken ct = default) => Task.FromResult(Allowed);
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
        public string? Authorization;
        public string ModelsResponse = """{"data":[]}""";
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(request.Method == HttpMethod.Get ? ModelsResponse : """{"choices":[{"message":{"role":"assistant","content":"OK"}}]}""") });
        }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public readonly User User = new(); public readonly Tenant Tenant = new(); public readonly Permissions Permissions = new(); public readonly Http Http = new();
        public readonly Guid ProjectId = Guid.NewGuid();
        public readonly Guid ConnectionId = Guid.NewGuid();
        public PlaceContextService Service => new(new ToolDispatcher(ProjectId), null!, null!, null!);
        public ServiceProvider Provider { get; }
        public DbBuildChatStore Store { get; }
        public ModelConnections Connections { get; }
        public IMembershipService Members { get; } = DispatchProxy.Create<IMembershipService, MembersProxy>();
        public Fixture(IBuildModelGateway? gateway = null)
        {
            var services = new ServiceCollection(); var database = Guid.NewGuid().ToString();
            services.AddSingleton<ICurrentTenant>(Tenant);
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(database));
            var encryption = new DataProtectionEncryptor(new EphemeralDataProtectionProvider());
            services.AddSingleton<IDataEncryptor>(encryption);
            services.AddScoped<IBuildChatStore>(_ => Store!);
            services.AddScoped(_ => new BuildChatAgent(Store!, gateway ?? new ScriptedModel(), Service, Permissions, Tenant));
            Provider = services.BuildServiceProvider();
            Store = new(Provider.GetRequiredService<IServiceScopeFactory>(), User, Tenant, Permissions, encryption);
            Connections = new(Provider.GetRequiredService<IServiceScopeFactory>(), User, Tenant, Permissions, Members, encryption, Http);
        }
        public async Task SeedAsync()
        {
            using var scope = Provider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Projects.Add(new() { Id = ProjectId, Name = "test", Path = "/tmp", Status = "Registered" });
            db.Tenants.Add(new() { Id = Tenant.TenantId, Name = "Test", Slug = "test" });
            db.Users.Add(new() { Id = User.UserId, Role = User.Role, Email = "test@example.test" });
            db.ModelConnections.Add(new() { Id = ConnectionId, Name = "Test", BaseUrl = "http://localhost:11434/v1", ModelsJson = "[\"test\"]" });
            await db.SaveChangesAsync();
        }
        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }
}
