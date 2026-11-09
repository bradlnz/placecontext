using PlaceContext.Application;
using PlaceContext.Application.Cqrs;
using PlaceContext.Host.Components.ViewModels;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Host.Components.Shared;

namespace PlaceContext.Host.Tests;

public sealed class WorkflowAgentActivityTests
{
    [Fact]
    public async Task Live_activity_shows_wait_tool_timing_and_run_link_and_escapes_model_text()
    {
        var now = DateTimeOffset.UtcNow;
        var run = Guid.NewGuid();
        var step = new ChainStepRunView(1, 1, 0, Guid.Empty, "Agent task", null, "Running", now, null,
            AgentActivity: [new(now, "tool_completed", "Tool completed", 1, "run_job", Guid.NewGuid(), run, 3.2),
                new(now, "update", "<script>alert(1)</script>"), new(now, "model", "Waiting for model", Turn: 2)]);
        var html = await RenderAsync(step);
        Assert.Contains("Agent activity", html);
        Assert.Contains("Waiting for model", html);
        Assert.Contains("since last update", html);
        Assert.Contains("run_job", html);
        Assert.Contains("Agent terminal output", html);
        Assert.DoesNotContain("<details", html);
        Assert.True(html.IndexOf("Tool completed", StringComparison.Ordinal) < html.IndexOf("Waiting for model", StringComparison.Ordinal));
        Assert.Contains("3.2s", html);
        Assert.Contains("View logs · " + run.ToString()[..8], html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public async Task Legacy_and_failed_steps_do_not_show_a_live_wait()
    {
        var step = new ChainStepRunView(0, 0, 0, Guid.Empty, "Agent task", null, "Failed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            Error: "Model unavailable");
        var html = await RenderAsync(step);
        Assert.Contains("No agent activity was recorded", html);
        Assert.Contains("Model unavailable", html);
        Assert.DoesNotContain("since last update", html);
    }

    [Fact]
    public async Task Agent_job_links_select_existing_log_panel_and_deduplicate_run_ids()
    {
        var now = DateTimeOffset.UtcNow;
        var jobRun = new JobRunDetailView(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Succeeded", now, now, [], null,
            new("code", "test", null, null, null, 1, 1, false));
        var activity = new AgentActivity(now, "tool_completed", "Tool completed", 1, "run_job", jobRun.JobId, jobRun.Id);
        var run = new ChainRunView(Guid.NewGuid(), Guid.NewGuid(), "Test", "Succeeded",
            [new(0, 0, 0, Guid.Empty, "Agent task", null, "Succeeded", now, now, AgentActivity: [activity with { Kind = "job_started" }, activity])], "{}", now, now);
        var vm = new JobChainsViewModel(new PlaceContextService(new Dispatcher(jobRun), null!, null!, null!), null!, null!, null!);
        await vm.OpenChainRunAsync(run);
        Assert.Equal("Completed", Assert.Single(vm.LogSteps).Status);
        await vm.OpenAgentJobRunAsync(jobRun.Id);
        Assert.Equal(jobRun.Id, vm.SelectedStepRunId);
        Assert.Equal(jobRun, vm.StepRunDetail);
        vm.CloseChainRun();
    }

    [Fact]
    public async Task Running_agent_child_is_selected_for_live_logs_before_it_completes()
    {
        var now = DateTimeOffset.UtcNow;
        var child = new JobRunDetailView(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Running", now, null, [], null,
            new("code", "test", null, null, null, 1, 1, false));
        var run = new ChainRunView(Guid.NewGuid(), Guid.NewGuid(), "Test", "Running",
            [new(0, 0, 0, Guid.Empty, "Agent task", null, "Running", now, null,
                AgentActivity: [new(now, "job_started", "Job submitted", 1, "run_job", child.JobId, child.Id)])], "{}", now, null);
        var vm = new JobChainsViewModel(new PlaceContextService(new Dispatcher(child), null!, null!, null!), null!, null!, null!);
        try
        {
            await vm.OpenChainRunAsync(run);
            Assert.Equal("Running", Assert.Single(vm.LogSteps).Status);
            Assert.Equal(child.Id, vm.SelectedStepRunId);
            Assert.Equal(child, vm.StepRunDetail);
        }
        finally { vm.CloseChainRun(); }
    }

    [Fact]
    public async Task Execution_modal_scopes_logs_to_the_clicked_step_and_clears_previous_output()
    {
        var now = DateTimeOffset.UtcNow;
        var child = new JobRunDetailView(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Succeeded", now, now, [], null,
            new("code", "test", null, null, null, 1, 1, false));
        var job = new ChainStepRunView(0, 0, 0, child.JobId, "Prepare", child.Id, "Succeeded", now, now);
        var agent = new ChainStepRunView(1, 1, 0, Guid.Empty, "Agent", null, "Failed", now, now, ActionType: LoopChainAction.TaskType);
        var run = new ChainRunView(Guid.NewGuid(), Guid.NewGuid(), "Test", "Failed", [job, agent], "{}", now, now);
        var vm = new JobChainsViewModel(new PlaceContextService(new Dispatcher(child), null!, null!, null!), null!, null!, null!);
        await vm.OpenChainRunAsync(run);
        await vm.OpenExecutionStepAsync(job);
        Assert.Equal(job, vm.ExecutionStep);
        Assert.Equal(child, vm.StepRunDetail);
        await vm.OpenExecutionStepAsync(agent);
        Assert.Equal(agent, vm.ExecutionStep);
        Assert.Empty(vm.ExecutionLogSteps);
        Assert.Null(vm.StepRunDetail);
        Assert.Null(vm.SelectedStepRunId);
        vm.CloseExecutionStep();
        Assert.Null(vm.ExecutionStep);
        Assert.Equal(run, vm.OpenRun);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Agent_nodes_can_open_details_without_a_job_run_when_the_viewer_supports_it(bool inspectActions)
    {
        await using var services = new ServiceCollection().AddLogging().AddTransient<ChainPipelineViewModel>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var step = new ChainStepRunView(0, 0, 0, Guid.Empty, "Agent", null, "Running", DateTimeOffset.UtcNow, null,
            ActionType: LoopChainAction.TaskType);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<WorkflowRunStep>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Step"] = step, ["InspectActions"] = inspectActions,
                ["OnStepClick"] = EventCallback.Factory.Create<ChainStepRunView>(this, _ => Task.CompletedTask)
            }))).ToHtmlString());
        Assert.Equal(!inspectActions, html.Contains("disabled"));
        Assert.Contains("Agent", html);
    }

    private sealed class Dispatcher(JobRunDetailView run) : IDispatcher
    {
        public Task<T> Send<T>(ICommand<T> command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<T> Query<T>(IQuery<T> query, CancellationToken ct = default) => Task.FromResult((T)(object)run);
    }

    private sealed class NoJs : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => throw new NotSupportedException();
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken ct, object?[]? args) => throw new NotSupportedException();
    }

    private static async Task<string> RenderAsync(ChainStepRunView step)
    {
        await using var services = new ServiceCollection().AddLogging().AddSingleton<IJSRuntime>(new NoJs()).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<WorkflowAgentActivity>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["Step"] = step }))).ToHtmlString());
    }
}
