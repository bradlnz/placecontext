using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Dtos;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed partial class BuildChatTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Dashboard_shows_the_human_question_and_resumes_the_same_run(bool yes)
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        var request = new ChainInputRequest(Guid.NewGuid(), [new(2, "Review", [new("approved")])],
            Kind: PlaceContext.Domain.ValueObjects.LoopChainAction.HumanType, Instructions: "Is the report ready to publish?", ResponseType: PlaceContext.Domain.ValueObjects.LoopChainAction.YesNoResponse);
        var run = new ChainRunView(Guid.NewGuid(), Guid.NewGuid(), "Review report", "AwaitingInput", [], "{\"report\":\"Ready\"}", DateTimeOffset.UtcNow, null)
            { ProjectId = f.ProjectId, InputRequest = request };
        var dispatcher = new ToolDispatcher(f.ProjectId) { Run = run };
        var queue = new SubmissionQueue(f.ProjectId, run.ChainId) { OnResume = () => dispatcher.Run = run with { Status = "Waiting", InputRequest = null } };
        var vm = new BuildChatViewModel(f.Store, f.Connections, null!, new(), new NoJs(), new(dispatcher, null!, null!, null!), f.Permissions, submissions: queue);
        await vm.LoadAsync(f.ProjectId, runId: run.Id);
        Assert.Equal(request, vm.HumanInputRequest);
        var services = new ServiceCollection().AddLogging().AddSingleton(vm).AddSingleton<IJSRuntime, NoJs>()
            .AddTransient(_ => new RunOutputViewModel(new NoJs()));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<PlaceContext.Host.Components.Pages.BuildChat>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { ["ProjectId"] = f.ProjectId, ["RunId"] = run.Id }))).ToHtmlString());
        Assert.Contains("Workflow question", html);
        Assert.Contains(request.Instructions!, html);
        Assert.DoesNotContain("Respond in the run view", html);
        await vm.SubmitHumanInputAsync(request.Id);
        Assert.Empty(queue.Responses);
        vm.SelectHumanAnswer(request.Id, yes);
        await vm.SubmitHumanInputAsync(request.Id);
        var response = Assert.Single(queue.Responses);
        Assert.Equal(run.Id, response.RunId);
        Assert.Equal(request.Id, response.RequestId);
        Assert.Equal(yes, JsonNode.Parse(response.Values)!["2"]!["approved"]!.GetValue<bool>());
        Assert.Equal(run.Id, vm.PreviewRun?.Id);
        Assert.Null(vm.HumanInputRequest);
        Assert.NotNull(vm.HumanInputNotice);
        await vm.SubmitHumanInputAsync(request.Id);
        Assert.Single(queue.Responses);
        await vm.DisposePageAsync();
    }

    [Fact]
    public async Task Dashboard_preserves_input_drafts_and_rejects_stale_or_unauthorized_responses()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        var request = new ChainInputRequest(Guid.NewGuid(), [new(1, "Review", [new("notes")])], Kind: PlaceContext.Domain.ValueObjects.LoopChainAction.HumanType, Instructions: "What needs changing?");
        var run = new ChainRunView(Guid.NewGuid(), Guid.NewGuid(), "Review", "AwaitingInput", [], null, DateTimeOffset.UtcNow, null)
            { ProjectId = f.ProjectId, InputRequest = request };
        var dispatcher = new ToolDispatcher(f.ProjectId) { Run = run };
        var queue = new SubmissionQueue(f.ProjectId, run.ChainId);
        var vm = new BuildChatViewModel(f.Store, f.Connections, null!, new(), new NoJs(), new(dispatcher, null!, null!, null!), f.Permissions, submissions: queue);
        await vm.LoadAsync(f.ProjectId, runId: run.Id);
        await vm.SubmitHumanInputAsync(request.Id);
        Assert.NotNull(vm.HumanInputError);
        Assert.Empty(queue.Responses);
        vm.SetHumanInput(1, "notes", "Add the source links.");
        await vm.RefreshPreviewRunAsync();
        Assert.Equal("Add the source links.", vm.GetHumanInput(1, "notes"));
        f.Permissions.Denied = Permission.JobsRun;
        await vm.SubmitHumanInputAsync(request.Id);
        Assert.Empty(queue.Responses);
        f.Permissions.Denied = null;
        var next = request with { Id = Guid.NewGuid() };
        dispatcher.Run = run with { InputRequest = next };
        await vm.RefreshPreviewRunAsync();
        Assert.Empty(vm.GetHumanInput(1, "notes"));
        await vm.SubmitHumanInputAsync(request.Id);
        Assert.Empty(queue.Responses);
        vm.SetHumanInput(1, "notes", "Ready.");
        queue.OnResume = () => dispatcher.Run = run with { Status = "Waiting", InputRequest = null };
        await vm.SubmitHumanInputAsync(next.Id);
        Assert.Equal("Ready.", JsonNode.Parse(Assert.Single(queue.Responses).Values)!["1"]!["notes"]!.GetValue<string>());
        await vm.DisposePageAsync();
    }
}
