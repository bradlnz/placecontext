using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Host.Components.Shared;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class ParallelWorkflowProgressTests
{
    [Fact]
    public async Task Canvas_shows_live_branch_statuses_without_needing_to_open_the_step()
    {
        var step = Step("Running", """[{"workflowName":"Triage","status":"Running"},{"workflowName":"Review","status":"Queued"},{"workflowName":"Support","status":"Skipped"},{"workflowName":"Billing","status":"Succeeded"}]""");
        var html = await RenderAsync<WorkflowRunStep>(new() { ["Step"] = step });
        Assert.Contains("Sub-workflow runs", html);
        foreach (var status in new[] { "Running", "Queued", "Skipped", "Succeeded" }) Assert.Contains($"data-status=\"{status}\"", html);
        Assert.Contains("Triage", html); Assert.Contains("Billing", html);
        Assert.DoesNotContain("Open sub-workflow run", html);
    }

    [Theory]
    [InlineData("Cancelled", "Cancelled")]
    [InlineData("Failed", "Interrupted")]
    public async Task Terminal_parent_never_leaves_a_branch_displayed_as_still_running(string parentStatus, string expected)
    {
        var html = await RenderAsync<SubWorkflowRunLink>(new() { ["Step"] = Step(parentStatus, """[{"workflowName":"Worker","status":"Running"}]""") });
        Assert.Contains($"data-status=\"{expected}\"", html);
        Assert.DoesNotContain("data-status=\"Running\"", html);
    }

    [Fact]
    public async Task Completed_branches_link_to_their_runs_and_old_missing_output_is_tolerated()
    {
        var project = Guid.NewGuid(); var workflow = Guid.NewGuid(); var run = Guid.NewGuid();
        var html = await RenderAsync<SubWorkflowRunLink>(new() { ["ProjectId"] = project, ["Step"] = Step("Succeeded",
            $$"""[{"workflowName":"Review","status":"Succeeded","workflowId":"{{workflow}}","runId":"{{run}}"}]""") });
        Assert.Contains($"/project/{project}/workflows/{workflow}/runs/{run}", html);
        var missing = await RenderAsync<WorkflowRunStep>(new() { ["Step"] = Step("Running", "invalid json") });
        Assert.Contains("Waiting for branch progress", missing);
    }

    private static ChainStepRunView Step(string status, string output) => new(0, 0, 0, Guid.Empty, "Parallel if gates", null, status,
        DateTimeOffset.UtcNow, null, ParallelWorkflowsChainAction.ActionType, Output: output);
    private static async Task<string> RenderAsync<T>(Dictionary<string, object?> parameters) where T : IComponent
    {
        await using var provider = new ServiceCollection().AddLogging().AddTransient<ChainPipelineViewModel>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters))).ToHtmlString());
    }
}
