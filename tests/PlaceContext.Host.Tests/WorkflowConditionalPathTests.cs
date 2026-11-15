using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Host.Components.Shared;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class WorkflowConditionalPathTests
{
    [Theory]
    [InlineData("all", false)]
    [InlineData("any", true)]
    public async Task Multiple_conditions_round_trip_and_cancel_preserves_saved_values(string mode, bool expected)
    {
        var vm = new JobChainsViewModel(new PlaceContextService(new RecordingDispatcher(), null!, null!, null!), null!, null!, new Navigation());
        vm.NewChain(); vm.AddStage(Guid.NewGuid()); vm.OpenGateEditor(0);
        vm.GateEditorType = "condition";
        vm.GateEditorMatch = mode;
        vm.GateEditorConditions[0].Operator = "gte";
        vm.GateEditorConditions[0].Path = "score";
        vm.GateEditorConditions[0].Value = "0.9";
        vm.AddCondition();
        vm.GateEditorConditions[1].Operator = "eq";
        vm.GateEditorConditions[1].Path = "approved";
        vm.GateEditorConditions[1].Value = "true";
        vm.SaveGate();
        var views = vm.BuildCanvasStageViews();
        await vm.OpenChainEditorAsync(new(Guid.NewGuid(), Guid.NewGuid(), "Gate", null, views, DateTimeOffset.UtcNow));
        vm.OpenGateEditor(0);
        Assert.Equal(mode, vm.GateEditorMatch); Assert.Equal(2, vm.GateEditorConditions.Count);
        vm.GateEditorConditions[0].Value = "0"; vm.RemoveCondition(vm.GateEditorConditions[1]); vm.CancelGateEditor();
        vm.OpenGateEditor(0);
        Assert.Equal("0.9", vm.GateEditorConditions[0].Value); Assert.Equal(2, vm.GateEditorConditions.Count);
        Assert.Equal(expected, Assert.IsType<ConditionGate>(vm.EdStageGates[0]).Evaluate("""{"score":0.8,"approved":true}""").Proceed);
    }

    [Fact]
    public void Gate_retry_inserts_a_bounded_repeat_before_delivery_and_preserves_all_conditions()
    {
        var vm = new JobChainsViewModel(new PlaceContextService(new RecordingDispatcher(), null!, null!, null!), null!, null!, new Navigation());
        vm.NewChain(); vm.AddStage(Guid.NewGuid()); vm.AddStage(Guid.NewGuid());
        vm.OpenGateEditor(1); vm.GateEditorType = "condition";
        vm.GateEditorConditions[0].Operator = "gte"; vm.GateEditorConditions[0].Path = "review.noul"; vm.GateEditorConditions[0].Value = "0.9";
        vm.AddCondition(); vm.GateEditorConditions[1].Operator = "eq"; vm.GateEditorConditions[1].Path = "approved"; vm.GateEditorConditions[1].Value = "true";
        var condition = vm.ConditionPreview();
        vm.RetryGateProcess(); vm.RepeatMaxAttempts = 3; vm.SaveRepeatAction();
        Assert.Null(vm.RepeatError); Assert.Equal(3, vm.EdStages.Count);
        var repeat = Assert.IsType<RepeatFlowChainAction>(vm.EdStageActions[1]);
        Assert.Equal(condition, repeat.UntilCondition); Assert.Equal(3, repeat.MaxAttempts);
        Assert.Equal(condition, Assert.IsType<ConditionGate>(vm.EdStageGates[2]).Expression);
        vm.OpenGateEditor(2); Assert.False(vm.CanRetryGate);
    }

    [Fact]
    public async Task Qemu_dialog_saves_updated_task_through_the_workflow_service()
    {
        var dispatcher = new RecordingDispatcher();
        var vm = new JobChainsViewModel(new PlaceContextService(dispatcher, null!, null!, null!), null!, null!, new Navigation());
        await vm.OpenChainEditorAsync(new(Guid.NewGuid(), Guid.NewGuid(), "Browser task", null,
            [new([], Action: new VirtualMachineChainActionView("browser-linux", "Original task", "browser", 2048, 2, 600, true))], DateTimeOffset.UtcNow));
        vm.EditQemuAction(0);
        vm.QemuCommand = "Updated task from the QEMU dialog";
        vm.QemuPauseForLogin = true;
        var loginId = Guid.NewGuid();
        vm.QemuLoginId = loginId.ToString();
        await vm.SaveQemuWorkflowAsync();
        Assert.Null(vm.QemuError);
        Assert.False(vm.ShowQemuEditor);
        Assert.False(vm.ShowEditor);
        var action = Assert.IsType<VirtualMachineChainAction>(Assert.Single(dispatcher.Saved!.StageActions!));
        Assert.True(action.PauseForLogin);
        Assert.Equal(loginId, action.LoginProfileId);
        Assert.Equal("Updated task from the QEMU dialog", action.Command);
        Assert.Equal("browser", action.OutputField);
        Assert.Equal(600, action.TimeoutSeconds);
        Assert.True(action.AllowNetwork);
    }

    [Fact]
    public async Task Qemu_save_validation_and_failure_keep_the_task_and_allow_retry_without_duplicate_steps()
    {
        var dispatcher = new RecordingDispatcher { SaveError = "Storage unavailable" };
        var vm = new JobChainsViewModel(new PlaceContextService(dispatcher, null!, null!, null!), null!, null!, new Navigation());
        await vm.OpenChainEditorAsync(new(Guid.NewGuid(), Guid.NewGuid(), "Browser task", null, [], DateTimeOffset.UtcNow));
        vm.OpenQemuActionAt(0);
        vm.VirtualMachineImageProfile = "browser-linux";
        vm.QemuCommand = "";
        await vm.SaveQemuWorkflowAsync();
        Assert.NotNull(vm.QemuError);
        Assert.Null(dispatcher.Saved);
        Assert.Empty(vm.EdStages);
        vm.QemuCommand = "Keep this task on failure";
        await vm.SaveQemuWorkflowAsync();
        Assert.Equal("Storage unavailable", vm.QemuError);
        Assert.True(vm.ShowQemuEditor);
        Assert.True(vm.ShowEditor);
        Assert.Equal("Keep this task on failure", vm.QemuCommand);
        dispatcher.SaveError = null;
        await vm.SaveQemuWorkflowAsync();
        Assert.Null(vm.QemuError);
        Assert.Single(vm.EdStages);
        Assert.Single(dispatcher.Saved!.StageActions!);
        Assert.False(vm.ShowEditor);
    }

    [Fact]
    public async Task Editing_and_saving_preserves_nested_false_paths_and_action_settings()
    {
        var fallback = Guid.NewGuid();
        var email = new SendEmailChainActionView("test@example.com", "Reviewer", "Review", "Check {{total}}", "attachments");
        var stage = new JobChainStageView([new(Guid.NewGuid(), "Approved")], new ConditionGateView("gte:review.noul:0.8"),
            [new([new(fallback, "Needs review")], new ConditionGateView("exists:backup"),
                [new([], Action: email)])]);
        var dispatcher = new RecordingDispatcher();
        var vm = new JobChainsViewModel(new PlaceContextService(dispatcher, null!, null!, null!), null!, null!, new Navigation());
        await vm.OpenChainEditorAsync(new(Guid.NewGuid(), Guid.NewGuid(), "Review", null, [stage], DateTimeOffset.UtcNow));
        vm.OpenGateEditor(0);
        vm.GateEditorConditions[0].Value = "0.9";
        vm.SaveGate();
        var preview = Assert.Single(vm.BuildCanvasStageViews());
        Assert.Equal("gte:review.noul:0.9", Assert.IsType<ConditionGateView>(preview.Gate).Expression);
        var nested = Assert.Single(Assert.Single(preview.ElseBranch!).ElseBranch!);
        Assert.Equal(email, nested.Action);
        await vm.SaveChainAsync();
        Assert.Null(vm.EditorError);
        var savedGate = Assert.IsType<ConditionGate>(Assert.Single(dispatcher.Saved!.StageGates!));
        var savedFallback = Assert.Single(savedGate.ElseBranch!);
        Assert.Equal(fallback, Assert.Single(savedFallback.JobIds));
        var savedEmail = Assert.IsType<SendEmailChainAction>(Assert.Single(Assert.IsType<ConditionGate>(savedFallback.Gate).ElseBranch!).Action);
        Assert.Equal(email.AttachmentPath, savedEmail.AttachmentPath);
        Assert.Equal(email.Body, savedEmail.Body);
    }

    [Fact]
    public async Task False_path_edits_are_cancelable_and_follow_the_stage_when_moved()
    {
        var vm = new JobChainsViewModel(new PlaceContextService(new RecordingDispatcher(), null!, null!, null!), null!, null!, new Navigation());
        vm.NewChain();
        vm.AddStage(Guid.NewGuid());
        vm.AddStage(Guid.NewGuid());
        vm.OpenGateEditor(0);
        vm.GateEditorType = "condition";
        var other = Guid.NewGuid();
        vm.GateEditorElseJobId = other.ToString();
        vm.AddElseJob();
        vm.SaveGate();
        vm.OpenGateEditor(0);
        vm.RemoveElseStage(0);
        vm.CancelGateEditor();
        vm.MoveStage(0, 1);
        vm.OpenGateEditor(1);
        Assert.Equal(other, Assert.Single(Assert.Single(vm.GateEditorElseStages).JobIds));
        var stages = vm.BuildCanvasStageViews();
        var reopened = new JobChainsViewModel(new PlaceContextService(new RecordingDispatcher(), null!, null!, null!), null!, null!, new Navigation());
        await reopened.OpenChainEditorAsync(new(Guid.NewGuid(), Guid.NewGuid(), "Review", null, stages, DateTimeOffset.UtcNow));
        reopened.OpenGateEditor(1);
        reopened.RemoveElseStage(0);
        reopened.SaveGate();
        Assert.Null(Assert.IsType<ConditionGate>(reopened.EdStageGates[1]).ElseBranch);
    }

    [Fact]
    public async Task Canvas_shows_both_conditional_routes_and_labels_parallel_jobs()
    {
        var stages = new[] { new JobChainStageView([new(Guid.NewGuid(), "Approved")], new ConditionGateView("gte:review.noul:0.8"),
            [new([new(Guid.NewGuid(), "Needs review")])]) };
        var services = new ServiceCollection().AddLogging().AddTransient<ChainCanvasViewModel>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var result = await renderer.RenderComponentAsync<ChainCanvas>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Stages"] = stages,
                ["Gates"] = new Dictionary<int, ChainGateView> { [0] = stages[0].Gate! }
            }));
            return result.ToHtmlString();
        });
        Assert.Contains("stage-caption", html);
        Assert.Contains("Run this job with the current workflow context", html);
        Assert.Contains("True", html);
        Assert.Contains("False", html);
        Assert.Contains("Approved", html);
        Assert.Contains("Needs review", html);
        Assert.Contains("Rejoin", html);
        Assert.Contains("Edit false path", html);
        Assert.Contains("Add parallel job", html);
    }

    private sealed class Navigation : NavigationManager
    {
        public Navigation() => Initialize("http://localhost:7700/", "http://localhost:7700/");
        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            Uri = ToAbsoluteUri(uri).AbsoluteUri;
            NotifyLocationChanged(false);
        }
    }

    private sealed class RecordingDispatcher : IDispatcher
    {
        public UpdateJobChainCommand? Saved;
        public string? SaveError;
        public Task<T> Send<T>(ICommand<T> command, CancellationToken ct = default)
        {
            Saved = Assert.IsType<UpdateJobChainCommand>(command);
            if (SaveError is not null)
            {
                throw new InvalidOperationException(SaveError);
            }
            return Task.FromResult(default(T)!);
        }
        public Task<T> Query<T>(IQuery<T> query, CancellationToken ct = default) => Task.FromResult((T)(object)(query switch
        {
            ListChainRunsQuery => (object)Array.Empty<ChainRunView>(),
            ListJobChainsQuery => Array.Empty<JobChainView>(),
            _ => throw new NotSupportedException(),
        }));
    }
}
