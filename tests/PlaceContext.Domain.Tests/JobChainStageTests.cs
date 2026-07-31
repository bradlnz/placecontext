using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using Xunit;

namespace PlaceContext.Domain.Tests;

/// <summary>
/// Domain tests for the fan-out/fan-in chain model: <see cref="ChainStage"/> validation, the
/// <see cref="JobChain"/> stage list (and its backward-compatible linear adapters), and
/// <see cref="ChainRun"/>'s stage-aware step addressing + all-or-nothing status derivation.
/// All tests are purely in-process — no I/O, no infrastructure.
/// </summary>
public class JobChainStageTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid ProjectId = Guid.NewGuid();

    // ── ChainStage ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ChainStage_rejects_an_empty_job_list()
        => Assert.Throws<ArgumentException>(() => new ChainStage(Array.Empty<Guid>()));

    [Fact]
    public void ChainStage_rejects_an_empty_job_id()
        => Assert.Throws<ArgumentException>(() => new ChainStage(new[] { Guid.Empty }));

    [Fact]
    public void ChainStage_allows_the_same_job_to_repeat()
    {
        var id = Guid.NewGuid();
        var stage = new ChainStage(new[] { id, id });
        Assert.Equal(2, stage.JobIds.Count);
        Assert.True(stage.IsParallel);
    }

    [Fact]
    public void ChainStage_of_a_single_job_is_not_parallel()
        => Assert.False(ChainStage.Of(Guid.NewGuid()).IsParallel);

    [Fact]
    public void ChainStage_supports_a_typed_email_action_without_a_job_id()
    {
        var action = new SendEmailChainAction(
            "client@example.com", "Client", "Report ready", "Your report is ready.");

        var stage = ChainStage.ForAction(action);

        Assert.Empty(stage.JobIds);
        Assert.Same(action, stage.Action);
        Assert.Equal(1, stage.ExecutionCount);
        Assert.False(stage.IsParallel);
    }

    [Fact]
    public void SendEmail_action_validates_required_fields_and_literal_recipient()
    {
        Assert.Throws<ArgumentException>(() =>
            new SendEmailChainAction("not-an-email", "Client", "Subject", "Body"));
        Assert.Throws<ArgumentException>(() =>
            new SendEmailChainAction("client@example.com", "Client", " ", "Body"));
        Assert.Throws<ArgumentException>(() =>
            new SendEmailChainAction("client@example.com", "Client", "Subject", " "));
    }

    [Fact]
    public void SendSms_action_validates_international_literal_recipient()
    {
        var action = new SendSmsChainAction("+61412345678", "Report ready");
        Assert.Equal("+61412345678", action.Recipient);
        Assert.Throws<ArgumentException>(() => new SendSmsChainAction("0412345678", "Report ready"));
    }

    [Theory]
    [InlineData("notexists:missing", true)]
    [InlineData("neq:status:failed", true)]
    [InlineData("contains:message:ready", true)]
    [InlineData("starts:message:cluster", true)]
    [InlineData("ends:message:ready", true)]
    [InlineData("in:status:queued,ready,done", true)]
    [InlineData("empty:items", true)]
    [InlineData("notempty:status", true)]
    public void Condition_gate_supports_richer_path_operators(string expression, bool expected)
    {
        const string payload = """{"status":"ready","message":"Cluster is ready","items":[]}""";

        Assert.Equal(expected, new ConditionGate(expression).Evaluate(payload).Proceed);
    }

    // ── JobChain stages ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_with_stages_preserves_fan_out_shape()
    {
        var a = Guid.NewGuid(); var b1 = Guid.NewGuid(); var b2 = Guid.NewGuid(); var c = Guid.NewGuid();
        var stages = new[] { ChainStage.Of(a), new ChainStage(new[] { b1, b2 }), ChainStage.Of(c) };

        var chain = JobChain.Create(ProjectId, "fan-out", null, stages, T0);

        Assert.Equal(3, chain.Stages.Count);
        Assert.False(chain.Stages[0].IsParallel);
        Assert.True(chain.Stages[1].IsParallel);
        Assert.Equal(new[] { b1, b2 }, chain.Stages[1].JobIds);
        // Flattened view (backward-compat) lists every job across every stage, in run order.
        Assert.Equal(new[] { a, b1, b2, c }, chain.StepJobIds);
    }

    [Fact]
    public void Create_with_a_flat_id_list_is_every_stage_size_one()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var chain = JobChain.Create(ProjectId, "linear", null, new[] { a, b }, T0);

        Assert.Equal(2, chain.Stages.Count);
        Assert.All(chain.Stages, s => Assert.False(s.IsParallel));
        Assert.Equal(new[] { a, b }, chain.StepJobIds);
    }

    [Fact]
    public void Create_rejects_an_empty_stage_list()
        => Assert.Throws<ArgumentException>(() => JobChain.Create(ProjectId, "c", null, Array.Empty<ChainStage>(), T0));

    [Fact]
    public void Update_replaces_stages_and_preserves_identity()
    {
        var chain = JobChain.Create(ProjectId, "c", null, new[] { Guid.NewGuid() }, T0);
        var id = chain.Id;
        var b1 = Guid.NewGuid(); var b2 = Guid.NewGuid();

        chain.Update("renamed", "desc", new[] { new ChainStage(new[] { b1, b2 }) }, T0.AddMinutes(1));

        Assert.Equal(id, chain.Id);
        Assert.Equal("renamed", chain.Name);
        Assert.Single(chain.Stages);
        Assert.True(chain.Stages[0].IsParallel);
    }

    [Fact]
    public void Rehydrate_round_trips_stage_shape()
    {
        var a = Guid.NewGuid(); var b1 = Guid.NewGuid(); var b2 = Guid.NewGuid();
        var stages = new[] { ChainStage.Of(a), new ChainStage(new[] { b1, b2 }) };
        var id = Guid.NewGuid();

        var chain = JobChain.Rehydrate(id, ProjectId, "c", null, stages, T0, T0);

        Assert.Equal(id, chain.Id);
        Assert.Equal(2, chain.Stages.Count);
        Assert.True(chain.Stages[1].IsParallel);
    }

    // ── ChainRun stage-aware transitions ────────────────────────────────────────────────────────────

    private static JobChain FanOutJoinChain(out Guid a, out Guid b1, out Guid b2, out Guid join)
    {
        a = Guid.NewGuid(); b1 = Guid.NewGuid(); b2 = Guid.NewGuid(); join = Guid.NewGuid();
        return JobChain.Create(ProjectId, "fan-out-join", null,
            new[] { ChainStage.Of(a), new ChainStage(new[] { b1, b2 }), ChainStage.Of(join) }, T0);
    }

    [Fact]
    public void Start_assigns_stage_and_branch_index_per_step()
    {
        var chain = FanOutJoinChain(out _, out _, out _, out _);
        var names = new[] { "a", "b1", "b2", "join" };

        var run = ChainRun.Start(chain, names, T0);

        Assert.Equal(4, run.Steps.Count);
        Assert.Equal((0, 0), (run.Steps[0].StageIndex, run.Steps[0].BranchIndex));
        Assert.Equal((1, 0), (run.Steps[1].StageIndex, run.Steps[1].BranchIndex));
        Assert.Equal((1, 1), (run.Steps[2].StageIndex, run.Steps[2].BranchIndex));
        Assert.Equal((2, 0), (run.Steps[3].StageIndex, run.Steps[3].BranchIndex));
        Assert.Equal(3, run.StageCount);

        var byStage = run.StepsByStage;
        Assert.Equal(3, byStage.Count);
        Assert.Single(byStage[0]);
        Assert.Equal(2, byStage[1].Count); // the fan-out group
        Assert.Single(byStage[2]);         // the join
    }

    [Fact]
    public void Start_includes_typed_actions_in_stage_aware_run_history()
    {
        var jobId = Guid.NewGuid();
        var action = new SendEmailChainAction(
            "client@example.com", "Client", "Report", "Ready");
        var chain = JobChain.Create(ProjectId, "deliver", null,
            new[] { ChainStage.Of(jobId), ChainStage.ForAction(action) }, T0);

        var run = ChainRun.Start(chain, new[] { "report", "Send email" }, T0);

        Assert.Equal(2, run.Steps.Count);
        Assert.Equal(SendEmailChainAction.ActionType, run.Steps[1].ActionType);
        Assert.Equal(Guid.Empty, run.Steps[1].JobId);
        Assert.Equal(1, run.Steps[1].StageIndex);
    }

    [Fact]
    public void Branches_within_a_stage_can_be_marked_running_independently()
    {
        var chain = FanOutJoinChain(out _, out _, out _, out _);
        var run = ChainRun.Start(chain, new[] { "a", "b1", "b2", "join" }, T0);

        run.MarkStepRunning(1, Guid.NewGuid(), T0.AddSeconds(1));
        // b2 (index 2) is still pending — the stage isn't settled until every branch finishes.
        Assert.False(run.IsStageSettled(1));
        Assert.Equal(ChainStepStatus.Running, run.Steps[1].Status);
        Assert.Equal(ChainStepStatus.Pending, run.Steps[2].Status);

        run.MarkStepRunning(2, Guid.NewGuid(), T0.AddSeconds(1));
        Assert.Equal(ChainStepStatus.Running, run.Steps[2].Status);
    }

    [Fact]
    public void Stage_settles_only_once_every_branch_reaches_a_terminal_status()
    {
        var chain = FanOutJoinChain(out _, out _, out _, out _);
        var run = ChainRun.Start(chain, new[] { "a", "b1", "b2", "join" }, T0);
        run.MarkStepRunning(1, Guid.NewGuid(), T0);
        run.MarkStepRunning(2, Guid.NewGuid(), T0);

        run.MarkStepFinished(1, run.Steps[1].RunId, ChainStepStatus.Succeeded, T0.AddSeconds(2));
        Assert.False(run.IsStageSettled(1)); // b2 still running

        run.MarkStepFinished(2, run.Steps[2].RunId, ChainStepStatus.Succeeded, T0.AddSeconds(2));
        Assert.True(run.IsStageSettled(1));
        Assert.False(run.StageFailed(1));
        Assert.False(run.StagePartial(1));
    }

    [Fact]
    public void Any_branch_failure_marks_the_whole_stage_failed()
    {
        var chain = FanOutJoinChain(out _, out _, out _, out _);
        var run = ChainRun.Start(chain, new[] { "a", "b1", "b2", "join" }, T0);
        run.MarkStepRunning(1, Guid.NewGuid(), T0);
        run.MarkStepRunning(2, Guid.NewGuid(), T0);

        run.MarkStepFinished(1, run.Steps[1].RunId, ChainStepStatus.Succeeded, T0.AddSeconds(1));
        run.MarkStepFinished(2, run.Steps[2].RunId, ChainStepStatus.Failed, T0.AddSeconds(1));

        Assert.True(run.IsStageSettled(1));
        Assert.True(run.StageFailed(1));
    }

    [Fact]
    public void A_partial_branch_downgrades_without_failing_the_stage()
    {
        var chain = FanOutJoinChain(out _, out _, out _, out _);
        var run = ChainRun.Start(chain, new[] { "a", "b1", "b2", "join" }, T0);
        run.MarkStepRunning(1, Guid.NewGuid(), T0);
        run.MarkStepRunning(2, Guid.NewGuid(), T0);

        run.MarkStepFinished(1, run.Steps[1].RunId, ChainStepStatus.Partial, T0.AddSeconds(1));
        run.MarkStepFinished(2, run.Steps[2].RunId, ChainStepStatus.Succeeded, T0.AddSeconds(1));

        Assert.False(run.StageFailed(1));
        Assert.True(run.StagePartial(1));
    }

    [Fact]
    public void Complete_skips_the_join_when_the_fan_out_stage_never_finished_running()
    {
        var chain = FanOutJoinChain(out _, out _, out _, out _);
        var run = ChainRun.Start(chain, new[] { "a", "b1", "b2", "join" }, T0);

        // Stage 0 (a) finishes; stage 1 (b1, b2) fails on b2 — the handler stops before dispatching
        // the join, leaving it (and any settled-but-not-yet-marked-skipped branches) Pending.
        run.MarkStepRunning(0, Guid.NewGuid(), T0);
        run.MarkStepFinished(0, run.Steps[0].RunId, ChainStepStatus.Succeeded, T0.AddSeconds(1));
        run.MarkStepRunning(1, Guid.NewGuid(), T0.AddSeconds(1));
        run.MarkStepRunning(2, Guid.NewGuid(), T0.AddSeconds(1));
        run.MarkStepFinished(1, run.Steps[1].RunId, ChainStepStatus.Succeeded, T0.AddSeconds(2));
        run.MarkStepFinished(2, run.Steps[2].RunId, ChainStepStatus.Failed, T0.AddSeconds(2));

        run.Complete(ChainRunStatus.Failed, run.Steps[0].JobName, T0.AddSeconds(3));

        Assert.Equal(ChainRunStatus.Failed, run.Status);
        Assert.Equal(ChainStepStatus.Succeeded, run.Steps[0].Status);
        Assert.Equal(ChainStepStatus.Succeeded, run.Steps[1].Status);
        Assert.Equal(ChainStepStatus.Failed, run.Steps[2].Status);
        Assert.Equal(ChainStepStatus.Skipped, run.Steps[3].Status); // the join never ran
    }

    [Fact]
    public void MarkStepRunning_records_the_steps_run_id_up_front()
    {
        var chain = JobChain.Create(ProjectId, "pipeline", null, new[] { Guid.NewGuid() }, T0);
        var run = ChainRun.Start(chain, new[] { "extract" }, T0);
        var stepRunId = Guid.NewGuid();

        run.MarkStepRunning(0, stepRunId, T0.AddSeconds(1));

        Assert.Equal(stepRunId, run.Steps[0].RunId); // a live pipeline can link to the running step
        Assert.Equal(ChainStepStatus.Running, run.Steps[0].Status);
    }
}
