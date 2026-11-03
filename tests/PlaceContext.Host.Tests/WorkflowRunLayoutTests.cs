using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application;
using PlaceContext.Host.Components.Shared;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class WorkflowRunLayoutTests
{
    private static readonly Guid Approved = Guid.NewGuid(), Rejected = Guid.NewGuid(), Summary = Guid.NewGuid();
    private static readonly DateTimeOffset Started = DateTimeOffset.UtcNow;
    private static IReadOnlyList<JobChainStageView> Layout =>
    [
        new([new(Approved, "Approved")], new ConditionGateView("gte:review.noul:0.8"), [new([new(Rejected, "Needs review")])]),
        new([new(Summary, "Summary")]),
    ];

    [Fact]
    public void Appended_false_path_is_placed_before_the_join_and_keeps_recorded_names()
    {
        var run = Run(Step(0, 0, Approved, "Skipped"), Step(1, 1, Summary, "Succeeded"),
            Step(2, 2, Rejected, "Succeeded") with
            {
                JobName = "Name at execution"
            });
        var mapped = ChainPipelineViewModel.MatchSteps(run, Layout)!;
        Assert.Equal("Skipped", Assert.Single(mapped["0"]).Status);
        Assert.Equal("Name at execution", Assert.Single(mapped["0/else/0"]).JobName);
        Assert.Equal(Summary, Assert.Single(mapped["1"]).JobId);
    }

    [Fact]
    public void Untaken_paths_are_not_reported_as_successful_or_clickable()
    {
        var run = Run(Step(0, 0, Approved, "Succeeded"), Step(1, 1, Summary, "Succeeded"));
        var step = Assert.Single(ChainPipelineViewModel.MatchSteps(run, Layout)!["0/else/0"]);
        Assert.Equal("Not run", step.Status);
        Assert.Null(step.RunId);
        var active = run with
        {
            Status = "Running",
            FinishedAt = null
        };
        Assert.Equal("Pending", Assert.Single(ChainPipelineViewModel.MatchSteps(active, Layout)!["0/else/0"]).Status);
    }

    [Fact]
    public void Changed_or_ambiguous_layout_falls_back_instead_of_assigning_the_wrong_branch()
    {
        var run = Run(Step(0, 0, Approved, "Skipped"), Step(1, 1, Summary, "Succeeded"), Step(2, 2, Rejected, "Succeeded"));
        Assert.Null(ChainPipelineViewModel.MatchSteps(run, Layout.Reverse().ToList()));
        var ambiguous = Layout.ToArray();
        ambiguous[0] = ambiguous[0] with
        {
            ElseBranch = [new([new(Rejected, "One")]), new([new(Rejected, "Two")])]
        };
        Assert.Null(ChainPipelineViewModel.MatchSteps(run, ambiguous));
    }

    [Fact]
    public void Repeated_top_level_jobs_parallel_steps_and_nested_actions_keep_their_positions()
    {
        var email = new SendEmailChainActionView("test@example.com", "Reviewer", "Subject", "Body", "");
        IReadOnlyList<JobChainStageView> layout =
        [
            new([new(Approved, "First")], new ConditionGateView("false"),
                [new([new(Rejected, "Fallback")], new ConditionGateView("false"), [new([], Action: email)])]),
            new([new(Approved, "Repeat"), new(Summary, "Parallel")]),
        ];
        var run = Run(Step(0, 0, Approved, "Skipped"), Step(1, 1, Approved, "Succeeded"),
            Step(2, 1, Summary, "Failed") with
            {
                BranchIndex = 1
            },
            Step(3, 2, Guid.Empty, "Failed") with
            {
                ActionType = email.Type,
                Error = "Delivery failed"
            });
        var mapped = ChainPipelineViewModel.MatchSteps(run, layout)!;
        Assert.Equal(2, mapped["1"].Count);
        Assert.Equal("Delivery failed", Assert.Single(mapped["0/else/0/else/0"]).Error);
        Assert.Equal("Not run", Assert.Single(mapped["0/else/0"]).Status);
    }

    [Fact]
    public async Task Live_updates_are_not_overwritten_by_an_older_definition_request()
    {
        var dispatcher = new DelayedDispatcher();
        var vm = new ChainPipelineViewModel(new PlaceContextService(dispatcher, null!, null!, null!));
        var run = Run(Step(0, 0, Approved, "Running"), Step(1, 1, Summary, "Pending")) with
        {
            FinishedAt = null
        };
        var loading = vm.LoadAsync(run);
        var completed = run with
        {
            Steps = [Step(0, 0, Approved, "Succeeded"), Step(1, 1, Summary, "Succeeded")],
            FinishedAt = Started.AddSeconds(10)
        };
        await vm.LoadAsync(completed);
        dispatcher.Result.SetResult([new(run.ChainId, run.ProjectId, "Demo", null, Layout, Started)]);
        await loading;
        Assert.Equal("Succeeded", Assert.Single(vm.RunSteps!["0"]).Status);
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("changed")]
    [InlineData("unavailable")]
    public async Task Unmatched_definitions_keep_a_canvas_with_recorded_order_parallel_jobs_and_actions(string definition)
    {
        var dispatcher = new DelayedDispatcher();
        var vm = new ChainPipelineViewModel(new PlaceContextService(dispatcher, null!, null!, null!));
        var run = Run(Step(0, 0, Approved, "Skipped"),
            Step(1, 1, Summary, "Succeeded") with
            {
                StartedAt = Started.AddSeconds(5)
            },
            Step(2, 1, Approved, "Failed") with
            {
                BranchIndex = 1,
                StartedAt = Started.AddSeconds(5),
                Error = "Failed job"
            },
            Step(3, 2, Guid.Empty, "Succeeded") with
            {
                StartedAt = Started.AddSeconds(3),
                ActionType = "qemu",
                JobName = "QEMU environment"
            });
        var loading = vm.LoadAsync(run);
        Assert.NotNull(vm.Layout); // The canvas is usable while the definition is loading.
        void AssertDefinition()
        {
            if (definition == "unavailable")
            {
                dispatcher.Result.SetException(new InvalidOperationException("Offline"));
                return;
            }
            dispatcher.Result.SetResult(definition == "deleted" ? [] :
                    [new(run.ChainId, run.ProjectId, "Changed", null, Layout.Reverse().ToList(), Started)]);
        }
        AssertDefinition();
        await loading;
        Assert.True(vm.UsesRecordedLayout);
        Assert.Equal(3, vm.Layout!.Count);
        Assert.Equal("qemu", Assert.Single(vm.RunSteps!["1"]).ActionType);
        Assert.Equal(2, vm.RunSteps["2"].Count);
        Assert.True(vm.Layout[2].IsParallel);
        Assert.Equal("Failed job", vm.RunSteps["2"][1].Error);
        Assert.Equal(run.Steps[2].RunId, vm.RunSteps["2"][1].RunId);
        Assert.All(vm.Layout, stage => Assert.Null(stage.Gate));
    }

    [Fact]
    public void Gate_only_stages_do_not_require_a_recorded_job()
    {
        IReadOnlyList<JobChainStageView> layout = [new([], new WaitGateView(30)), new([new(Summary, "Summary")])];
        var mapped = ChainPipelineViewModel.MatchSteps(Run(Step(0, 1, Summary, "Succeeded")), layout);
        Assert.NotNull(mapped);
        Assert.Empty(mapped["0"]);
        Assert.Single(mapped["1"]);
    }

    [Fact]
    public async Task Missing_definition_renders_the_shared_read_only_canvas()
    {
        var services = new ServiceCollection().AddLogging().AddTransient<ChainCanvasViewModel>().AddTransient<ChainPipelineViewModel>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var result = await renderer.RenderComponentAsync<ChainPipeline>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Run"] = Run(Step(0, 0, Approved, "Failed"))
            }));
            return result.ToHtmlString();
        });
        Assert.Contains("run-canvas", html);
        Assert.Contains("Recorded execution order", html);
        Assert.Contains("data-status=\"Failed\"", html);
        Assert.Contains("run-step-link", html);
        Assert.DoesNotContain("matching workflow layout is unavailable", html);
        Assert.DoesNotContain("Add parallel job", html);
    }

    [Fact]
    public void Fallback_places_appended_branch_before_the_later_summary()
    {
        var run = Run(Step(0, 0, Approved, "Skipped"),
            Step(1, 1, Summary, "Succeeded") with
            {
                StartedAt = Started.AddSeconds(5)
            },
            Step(2, 2, Rejected, "Succeeded") with
            {
                StartedAt = Started.AddSeconds(3)
            });
        Assert.Equal(new[] { Approved, Rejected, Summary }, new ChainPipelineViewModel().Stages(run).Select(group => group[0].JobId));
    }

    [Fact]
    public async Task Run_canvas_shows_statuses_errors_and_output_links_without_edit_controls()
    {
        var run = Run(Step(0, 0, Approved, "Skipped"), Step(1, 1, Summary, "Skipped"),
            Step(2, 2, Rejected, "Failed") with
            {
                Error = "Provider rejected the request"
            });
        var services = new ServiceCollection().AddLogging().AddTransient<ChainCanvasViewModel>().AddTransient<ChainPipelineViewModel>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var result = await renderer.RenderComponentAsync<ChainCanvas>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Stages"] = Layout,
                ["RunSteps"] = ChainPipelineViewModel.MatchSteps(run, Layout)
            }));
            return result.ToHtmlString();
        });
        Assert.Contains("True", html);
        Assert.Contains("False", html);
        Assert.Contains("Rejoin", html);
        Assert.Contains("data-status=\"Skipped\"", html);
        Assert.Contains("data-status=\"Failed\"", html);
        Assert.Contains("Provider rejected the request", html);
        Assert.Contains("Failed 2s", html);
        Assert.Contains("run-step-link", html);
        Assert.DoesNotContain("Edit false path", html);
        Assert.DoesNotContain("Add parallel job", html);
        Assert.DoesNotContain("Remove stage", html);
        Assert.DoesNotContain("draggable=\"true\"", html);
    }

    private static ChainRunView Run(params ChainStepRunView[] steps) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Demo", "Succeeded", steps, null, Started, Started.AddSeconds(10));

    private static ChainStepRunView Step(int index, int stage, Guid job, string status) =>
        new(index, stage, 0, job, job == Approved ? "Approved" : job == Rejected ? "Needs review" : "Summary",
            status == "Skipped" ? null : Guid.NewGuid(), status,
            status == "Skipped" ? null : Started, Started.AddSeconds(2));

    private sealed class DelayedDispatcher : IDispatcher
    {
        public TaskCompletionSource<IReadOnlyList<JobChainView>> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<T> Query<T>(IQuery<T> query, CancellationToken ct = default) => (T)(object)await Result.Task;
        public Task<T> Send<T>(ICommand<T> command, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
