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
    [InlineData(null)]
    [InlineData("Does this need review?")]
    public void False_path_editor_opens_requested_branch_and_regular_editor_resets_to_true(string? question)
    {
        var vm = new JobChainsViewModel(new PlaceContextService(new RecordingDispatcher(), null!, null!, null!), null!, null!, new Navigation());
        var fallback = Guid.NewGuid();
        vm.NewChain();
        vm.AddStage(Guid.NewGuid());
        vm.EdStageGates[0] = new ConditionGate("eq:ready:true", [ChainStage.Of(fallback)], question);

        vm.OpenGateEditor(0, falsePath: true);
        Assert.True(vm.ShowGateEditor);
        Assert.Equal("false", vm.GateEditorPath);
        Assert.Equal(fallback, Assert.Single(Assert.Single(vm.GateEditorElseStages).JobIds));
        Assert.Equal(question ?? "", vm.GateReviewQuestion);
        vm.CancelGateEditor();
        vm.OpenGateEditor(0);
        Assert.Equal("true", vm.GateEditorPath);
        Assert.Equal(fallback, Assert.Single(Assert.Single(vm.GateEditorElseStages).JobIds));
    }

    [Fact]
    public void False_path_limits_stay_in_gate_draft_until_the_gate_is_saved()
    {
        var vm = new JobChainsViewModel(null!, null!, null!, new Navigation());
        vm.NewChain(); vm.AddStage(Guid.NewGuid()); vm.OpenGateEditor(0); vm.GateEditorType = "condition";
        vm.GateEditorElseJobId = Guid.NewGuid().ToString(); vm.AddElseJob();
        vm.OpenElseStepPolicy(0); vm.StepMaxRetries = 3; vm.StepTimeoutSeconds = 60; vm.SaveStepPolicy();
        var policy = new StepExecutionPolicy(3, 5, 60);
        Assert.Equal(policy, vm.GateEditorElseStages[0].ExecutionPolicy);
        vm.SaveGate(); vm.OpenGateEditor(0);
        Assert.Equal(policy, vm.GateEditorElseStages[0].ExecutionPolicy);
        vm.OpenElseStepPolicy(0); vm.ResetStepPolicy(); vm.CancelGateEditor(); vm.OpenGateEditor(0);
        Assert.Equal(policy, vm.GateEditorElseStages[0].ExecutionPolicy);
    }

    [Fact]
    public async Task Step_limits_validate_follow_reordering_and_survive_save_and_reopen()
    {
        var dispatcher = new RecordingDispatcher();
        var vm = new JobChainsViewModel(new PlaceContextService(dispatcher, null!, null!, null!), null!, null!, new Navigation());
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        await vm.OpenChainEditorAsync(new(Guid.NewGuid(), Guid.NewGuid(), "Policies", null,
            [new([new(first, "First")]), new([new(second, "Second")])], DateTimeOffset.UtcNow));
        vm.OpenStepPolicy(0); vm.StepMaxRetries = 11; vm.SaveStepPolicy();
        Assert.NotNull(vm.StepPolicyError); Assert.Equal(0, vm.PolicyStageIndex); Assert.Null(vm.EdStagePolicies[0]);
        vm.StepMaxRetries = 2; vm.StepRetryDelaySeconds = 0; vm.StepTimeoutSeconds = 30; vm.SaveStepPolicy();
        Assert.Null(vm.PolicyStageIndex);
        var policy = new StepExecutionPolicy(2, 0, 30);
        vm.MoveStageTo(1, 0);
        Assert.Equal(policy, vm.BuildCanvasStageViews()[1].ExecutionPolicy);
        vm.RemoveStage(0);
        Assert.Equal(policy, vm.BuildCanvasStageViews()[0].ExecutionPolicy);
        await vm.SaveChainAsync(false);
        Assert.Equal(policy, Assert.Single(dispatcher.Saved!.StagePolicies!));
        var view = new JobChainView(Guid.NewGuid(), Guid.NewGuid(), "Saved", null, vm.BuildCanvasStageViews(), DateTimeOffset.UtcNow);
        await vm.OpenChainEditorAsync(view);
        vm.OpenStepPolicy(0); Assert.Equal(2, vm.StepMaxRetries); Assert.Equal(30, vm.StepTimeoutSeconds);
        vm.StepMaxRetries = 8; vm.CloseStepPolicy(); vm.OpenStepPolicy(0); Assert.Equal(2, vm.StepMaxRetries);
        vm.ResetStepPolicy(); Assert.Null(vm.BuildCanvasStageViews()[0].ExecutionPolicy);
    }

    [Fact]
    public async Task Adding_an_automatic_gate_at_the_end_creates_its_true_path_stage()
    {
        var vm = new JobChainsViewModel(new PlaceContextService(new RecordingDispatcher(), null!, null!, null!), null!, null!, new Navigation());
        vm.NewChain(); vm.AddStage(Guid.NewGuid()); vm.OpenStageNodePicker(1);
        await vm.ChooseAutomaticGateNode();
        Assert.True(vm.IsAutomaticGateEditor); vm.GateReviewQuestion = "Ready?";
        vm.SaveGate(); Assert.NotNull(vm.GateEditorError); Assert.Single(vm.EdStages);
        var job = Guid.NewGuid(); vm.GateEditorTrueJobId = job.ToString(); vm.SaveGate();
        Assert.Null(vm.GateEditorError); Assert.Equal(2, vm.EdStages.Count);
        Assert.Equal(job, Assert.Single(vm.EdStages[1]));
        Assert.Equal("Ready?", Assert.IsType<ConditionGateView>(vm.BuildCanvasStageViews()[1].Gate).ReviewQuestion);
    }

    [Fact]
    public async Task Automatic_if_validates_question_and_preserves_question_and_paths_when_reopened()
    {
        var vm = new JobChainsViewModel(new PlaceContextService(new RecordingDispatcher(), null!, null!, null!), null!, null!, new Navigation());
        vm.NewChain(); vm.AddStage(Guid.NewGuid());
        vm.OpenGateEditor(0); vm.GateEditorType = "automatic";
        vm.SaveGate(); Assert.NotNull(vm.GateEditorError); Assert.True(vm.ShowGateEditor);
        Assert.False(vm.EdStageGates.ContainsKey(0));
        vm.GateReviewQuestion = "Does this need technical support?";
        var fallback = Guid.NewGuid(); vm.GateEditorElseJobId = fallback.ToString(); vm.AddElseJob();
        vm.SaveGate(); Assert.Null(vm.GateEditorError); Assert.False(vm.ShowGateEditor);
        var views = vm.BuildCanvasStageViews();
        Assert.Equal(vm.GateReviewQuestion, Assert.IsType<ConditionGateView>(views[0].Gate).ReviewQuestion);
        await vm.OpenChainEditorAsync(new(Guid.NewGuid(), Guid.NewGuid(), "Automatic", null, views, DateTimeOffset.UtcNow));
        vm.OpenGateEditor(0);
        Assert.True(vm.IsAutomaticGateEditor); Assert.True(vm.IsConditionGateEditor);
        Assert.Equal("Does this need technical support?", vm.GateReviewQuestion);
        Assert.Equal(fallback, Assert.Single(Assert.Single(vm.GateEditorElseStages).JobIds));
        vm.GateReviewQuestion = "Changed"; vm.CancelGateEditor(); vm.OpenGateEditor(0);
        Assert.Equal("Does this need technical support?", vm.GateReviewQuestion);
        vm.GateEditorType = "condition"; vm.SaveGate();
        Assert.Null(Assert.IsType<ConditionGate>(vm.EdStageGates[0]).ReviewQuestion);
    }

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

    [Theory]
    [InlineData(null, false, false)]
    [InlineData("Ready?", false, false)]
    [InlineData(null, true, false)]
    [InlineData("Ready?", true, false)]
    [InlineData(null, false, true)]
    [InlineData("Ready?", false, true)]
    public async Task If_gate_remove_button_is_available_only_in_the_editor(string? question, bool readOnly, bool running)
    {
        var services = new ServiceCollection().AddLogging().AddAuthorizationCore().AddTransient<ChainCanvasViewModel>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var result = await renderer.RenderComponentAsync<ChainCanvas>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Stages"] = new[] { new JobChainStageView([], new ConditionGateView("true", ReviewQuestion: question), Action: new QueryDataChainActionView("SELECT 1", "data")) },
                ["ReadOnly"] = readOnly,
                ["RunSteps"] = running ? new Dictionary<string, IReadOnlyList<ChainStepRunView>>() : null,
                ["OnRemoveGate"] = EventCallback.Factory.Create<int>(this, _ => { })
            }));
            return result.ToHtmlString();
        });
        Assert.Equal(!readOnly && !running, html.Contains("Remove if gate from step 1"));
        Assert.Contains($">{(question is null ? "IF" : "Determine")}</span>", html);
        Assert.DoesNotContain(">Review</span>", html);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Canvas_shows_three_tree_branches_with_nested_gates_and_edit_controls(bool readOnly)
    {
        var child = new JobChainView(Guid.NewGuid(), Guid.NewGuid(), "Branch actions", null,
            [new([new(Guid.NewGuid(), "Approved action")], new ConditionGateView("eq:ready:true"),
                [new([new(Guid.NewGuid(), "Fallback action")])])], DateTimeOffset.UtcNow);
        var paths = new ParallelWorkflowsChainActionView([
            new(child.Id, child.Name, "eq:kind:a"), new(child.Id, child.Name, "eq:kind:b"),
            new(child.Id, child.Name, "eq:kind:c", WhenFalse: true)]);
        await using var provider = new ServiceCollection().AddLogging().AddAuthorizationCore().AddTransient<ChainCanvasViewModel>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<ChainCanvas>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Stages"] = new[] { new JobChainStageView([], Action: paths) },
                ["Workflows"] = new[] { child },
                ["ReadOnly"] = readOnly,
                ["OnDeleteBranch"] = EventCallback.Factory.Create<(int, int)>(this, _ => { }),
                ["OnEditBranchActions"] = EventCallback.Factory.Create<(int, int)>(this, _ => { })
            }));
            return rendered.ToHtmlString();
        });
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(html, "class=\"path-lane\"").Count);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(html, "class=\"path-card\"").Count);
        Assert.Equal(readOnly ? 0 : 3, System.Text.RegularExpressions.Regex.Matches(html, "Edit actions").Count);
        Assert.Equal(readOnly ? 0 : 3, System.Text.RegularExpressions.Regex.Matches(html, "aria-label=\"Delete branch").Count);
        Assert.DoesNotContain("path-outcomes", html);
        Assert.Contains("Approved action", html);
        Assert.Contains("Fallback action", html);
        Assert.Contains("nested-paths", html);
        Assert.Contains("Join all matching branches", html);
        Assert.Contains("eq:kind:c", html);
    }

    [Fact]
    public async Task Canvas_shows_both_conditional_routes_and_labels_parallel_jobs()
    {
        var stages = new[] { new JobChainStageView([new(Guid.NewGuid(), "Approved")], new ConditionGateView("gte:review.noul:0.8"),
            [new([new(Guid.NewGuid(), "Needs review")])]) };
        var services = new ServiceCollection().AddLogging().AddAuthorizationCore().AddTransient<ChainCanvasViewModel>();
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
