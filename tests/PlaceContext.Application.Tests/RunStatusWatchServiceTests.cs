using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

/// <summary>
/// Unit tests for the run-status watcher sweep: Running is notified once, terminal transitions
/// are notified promptly and exactly once (despite the cursor overlap re-reading rows), chain
/// progress updates as steps advance, chain-owned job runs are folded into the chain's own
/// notification, and historic runs stay out of a fresh pane.
/// </summary>
public class RunStatusWatchServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Project = Guid.NewGuid();

    private sealed class Ctx
    {
        public FakeRunStatusReader Reader { get; } = new();
        public FakeRunStatusNotifier Notifier { get; } = new();
        public FakeClock Clock { get; } = new(T0);
        public RunWatchState State { get; } = new();
        public RunStatusWatchService Service { get; }

        public Ctx() => Service = new RunStatusWatchService(Reader, Notifier, Clock);

        public Task Sweep() => Service.SweepAsync(State);
    }

    private static JobRunStatusRow JobRow(Guid id, JobRunStatus status, DateTimeOffset started,
        DateTimeOffset? finished = null, string name = "encode")
        => new(id, Guid.NewGuid(), name, Project, status, started, finished);

    private static ChainRunStatusRow ChainRow(Guid id, ChainRunStatus status, int total, int done,
        string? currentStep, IReadOnlyList<Guid>? stepRunIds = null, DateTimeOffset? finished = null)
        => new(id, Guid.NewGuid(), "nightly", Project, status, total, done, currentStep,
            stepRunIds ?? Array.Empty<Guid>(), T0, finished);

    // ── Job runs ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunningJobRun_IsNotifiedOnce_AcrossSweeps()
    {
        var ctx = new Ctx();
        var runId = Guid.NewGuid();
        ctx.Reader.JobRuns.Add(JobRow(runId, JobRunStatus.Running, T0));

        await ctx.Sweep();
        ctx.Clock.UtcNow = T0.AddSeconds(2);
        await ctx.Sweep();

        var update = Assert.Single(ctx.Notifier.Synced);
        Assert.Equal(RunOutcome.Running, update.Outcome);
        Assert.Equal(RunStatusWatchService.JobRunKey(runId), update.Key);
        Assert.Equal("Run job — encode", update.Title);
    }

    [Theory]
    [InlineData(JobRunStatus.Succeeded, RunOutcome.Succeeded)]
    [InlineData(JobRunStatus.Partial, RunOutcome.Partial)]
    [InlineData(JobRunStatus.Failed, RunOutcome.Failed)]
    public async Task TerminalTransition_IsNotifiedWithMatchingOutcome(JobRunStatus status, RunOutcome expected)
    {
        var ctx = new Ctx();
        var runId = Guid.NewGuid();
        ctx.Reader.JobRuns.Add(JobRow(runId, JobRunStatus.Running, T0));
        await ctx.Sweep();

        ctx.Reader.JobRuns[0] = JobRow(runId, status, T0, finished: T0.AddSeconds(30));
        ctx.Clock.UtcNow = T0.AddSeconds(32);
        await ctx.Sweep();

        Assert.Equal(2, ctx.Notifier.Synced.Count);
        var terminal = ctx.Notifier.Synced[1];
        Assert.Equal(expected, terminal.Outcome);
        Assert.Equal($"run finished — {status}", terminal.Detail);
        Assert.Equal(T0.AddSeconds(30), terminal.FinishedAt);
    }

    [Fact]
    public async Task TerminalRow_ReadAgainByCursorOverlap_IsNotRenotified()
    {
        var ctx = new Ctx();
        var runId = Guid.NewGuid();
        ctx.Reader.JobRuns.Add(JobRow(runId, JobRunStatus.Succeeded, T0, finished: T0.AddSeconds(1)));

        await ctx.Sweep(); // notifies the fresh terminal run
        ctx.Clock.UtcNow = T0.AddSeconds(4);
        await ctx.Sweep(); // overlap window still returns the row
        ctx.Clock.UtcNow = T0.AddSeconds(8);
        await ctx.Sweep();

        Assert.Single(ctx.Notifier.Synced);
    }

    [Fact]
    public async Task FastRun_FinishingBetweenSweeps_StillGetsTerminalNotification()
    {
        var ctx = new Ctx();
        await ctx.Sweep(); // establish the cursor with nothing running

        var runId = Guid.NewGuid();
        ctx.Clock.UtcNow = T0.AddSeconds(10);
        ctx.Reader.JobRuns.Add(JobRow(runId, JobRunStatus.Failed, T0.AddSeconds(5), finished: T0.AddSeconds(9)));
        await ctx.Sweep();

        var update = Assert.Single(ctx.Notifier.Synced);
        Assert.Equal(RunOutcome.Failed, update.Outcome);
    }

    [Fact]
    public async Task HistoricRuns_FinishedBeforeTheStartupLookback_AreNotResurfaced()
    {
        var ctx = new Ctx();
        ctx.Reader.JobRuns.Add(JobRow(Guid.NewGuid(), JobRunStatus.Succeeded,
            T0.AddHours(-2), finished: T0.AddHours(-1)));

        await ctx.Sweep();

        Assert.Empty(ctx.Notifier.Synced);
    }

    [Fact]
    public async Task RecentlyFinishedRun_WithinTheStartupLookback_IsRestoredAfterRestart()
    {
        var ctx = new Ctx(); // fresh state = fresh replica
        var runId = Guid.NewGuid();
        ctx.Reader.JobRuns.Add(JobRow(runId, JobRunStatus.Succeeded, T0.AddMinutes(-6), finished: T0.AddMinutes(-5)));

        await ctx.Sweep();

        var update = Assert.Single(ctx.Notifier.Synced);
        Assert.Equal(RunOutcome.Succeeded, update.Outcome);
        Assert.Equal(RunStatusWatchService.JobRunKey(runId), update.Key);
    }

    [Fact]
    public async Task RunStuckInRunning_ThenReapedToFailed_IsNotifiedFailed()
    {
        var ctx = new Ctx();
        var runId = Guid.NewGuid();
        ctx.Reader.JobRuns.Add(JobRow(runId, JobRunStatus.Running, T0));
        await ctx.Sweep();

        // The orphan reaper flips the abandoned row to Failed; the watcher must surface that.
        ctx.Clock.UtcNow = T0.AddHours(1);
        ctx.Reader.JobRuns[0] = JobRow(runId, JobRunStatus.Failed, T0, finished: T0.AddHours(1));
        await ctx.Sweep();

        Assert.Equal(RunOutcome.Failed, ctx.Notifier.Synced[^1].Outcome);
    }

    // ── Chain runs ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChainRun_NotifiesProgress_OnEachStepAdvance_AndThenTerminal()
    {
        var ctx = new Ctx();
        var chainId = Guid.NewGuid();
        ctx.Reader.ChainRuns.Add(ChainRow(chainId, ChainRunStatus.Running, total: 3, done: 0, currentStep: "extract"));
        await ctx.Sweep();

        ctx.Clock.UtcNow = T0.AddSeconds(2);
        await ctx.Sweep(); // no step change → no extra notification

        ctx.Reader.ChainRuns[0] = ChainRow(chainId, ChainRunStatus.Running, total: 3, done: 1, currentStep: "transform");
        ctx.Clock.UtcNow = T0.AddSeconds(4);
        await ctx.Sweep();

        ctx.Reader.ChainRuns[0] = ChainRow(chainId, ChainRunStatus.Succeeded, total: 3, done: 3,
            currentStep: null, finished: T0.AddSeconds(5));
        ctx.Clock.UtcNow = T0.AddSeconds(6);
        await ctx.Sweep();

        Assert.Equal(3, ctx.Notifier.Synced.Count);
        Assert.Equal("step 1/3 — extract", ctx.Notifier.Synced[0].Detail);
        Assert.Equal("step 2/3 — transform", ctx.Notifier.Synced[1].Detail);
        Assert.Equal(RunOutcome.Succeeded, ctx.Notifier.Synced[2].Outcome);
        Assert.Equal("chain finished — Succeeded (3 step(s) ran)", ctx.Notifier.Synced[2].Detail);
        Assert.All(ctx.Notifier.Synced, u => Assert.Equal(RunStatusWatchService.ChainRunKey(chainId), u.Key));
    }

    [Fact]
    public async Task JobRunsOwnedByAChainStep_AreFoldedIntoTheChainNotification()
    {
        var ctx = new Ctx();
        var stepRunId = Guid.NewGuid();
        ctx.Reader.ChainRuns.Add(ChainRow(Guid.NewGuid(), ChainRunStatus.Running, total: 2, done: 0,
            currentStep: "extract", stepRunIds: new[] { stepRunId }));
        ctx.Reader.JobRuns.Add(JobRow(stepRunId, JobRunStatus.Running, T0));
        await ctx.Sweep();

        // The step run finishing must not produce its own notification either.
        ctx.Reader.JobRuns[0] = JobRow(stepRunId, JobRunStatus.Succeeded, T0, finished: T0.AddSeconds(1));
        ctx.Clock.UtcNow = T0.AddSeconds(2);
        await ctx.Sweep();

        Assert.All(ctx.Notifier.Synced, u => Assert.StartsWith("chain-run:", u.Key));
    }

    [Fact]
    public async Task StandaloneJobRun_AlongsideAChain_IsStillNotified()
    {
        var ctx = new Ctx();
        var standalone = Guid.NewGuid();
        ctx.Reader.ChainRuns.Add(ChainRow(Guid.NewGuid(), ChainRunStatus.Running, total: 1, done: 0,
            currentStep: "extract", stepRunIds: new[] { Guid.NewGuid() }));
        ctx.Reader.JobRuns.Add(JobRow(standalone, JobRunStatus.Running, T0));

        await ctx.Sweep();

        Assert.Contains(ctx.Notifier.Synced, u => u.Key == RunStatusWatchService.JobRunKey(standalone));
    }

    // ── State hygiene ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DedupeMemory_IsEvicted_AfterTheRetentionWindow()
    {
        var ctx = new Ctx();
        var runId = Guid.NewGuid();
        ctx.Reader.JobRuns.Add(JobRow(runId, JobRunStatus.Succeeded, T0, finished: T0.AddSeconds(1)));
        await ctx.Sweep();
        Assert.Single(ctx.State.NotifiedTerminal);

        ctx.Clock.UtcNow = T0 + RunStatusWatchService.MemoryRetention + TimeSpan.FromMinutes(1);
        await ctx.Sweep();

        Assert.Empty(ctx.State.NotifiedTerminal);
        Assert.Single(ctx.Notifier.Synced); // eviction never re-notifies: the row left the window too
    }

    [Fact]
    public async Task RunningRun_ThatVanishesWithoutATerminalRow_IsForgotten()
    {
        var ctx = new Ctx();
        var runId = Guid.NewGuid();
        ctx.Reader.JobRuns.Add(JobRow(runId, JobRunStatus.Running, T0));
        await ctx.Sweep();
        Assert.Single(ctx.State.JobRuns);

        ctx.Reader.JobRuns.Clear(); // e.g. the project (and its runs) was deleted
        ctx.Clock.UtcNow = T0.AddSeconds(2);
        await ctx.Sweep();

        Assert.Empty(ctx.State.JobRuns);
    }
}
