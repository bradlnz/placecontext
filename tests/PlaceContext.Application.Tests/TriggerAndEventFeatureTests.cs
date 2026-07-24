using PlaceContext.Application.Features;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

/// <summary>
/// Application-layer tests for triggers and events: creating schedule/event triggers, event fan-out
/// enqueuing runs, schedule scanning advancing next-run, and event-type listing including built-ins.
/// Unit tests over in-memory fakes.
/// </summary>
public class TriggerAndEventFeatureTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    private sealed class Ctx
    {
        public InMemoryJobRepository Jobs = new();
        public InMemoryJobChainRepository Chains = new();
        public InMemoryJobTriggerRepository Triggers = new();
        public InMemoryEventRepository Events = new();
        public FakeJobRunQueue Queue = new();
        public FakeCronSchedule Cron = new();
        public FakeCurrentTenant Tenant = new(TenantId);
        public RecordingUnitOfWork Uow = new();
        public FakeClock Clock = new(T0);

        public Job SeedJob()
        {
            var map = new MapSpec("img/worker:latest", new[] { "{}" }, new Dictionary<string, string>());
            var job = Job.Create(Guid.NewGuid(), "nightly-report", null, map, null, 1, ExitCodePolicy.Default, T0);
            Jobs.AddAsync(job).GetAwaiter().GetResult();
            return job;
        }

        public EventDispatchService Dispatch() =>
            new(Events, Triggers, Queue, Tenant, Uow, Clock);
    }

    // ── CreateTrigger ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateTrigger_schedule_computes_next_run_from_cron()
    {
        var c = new Ctx();
        var job = c.SeedJob();
        var handler = new CreateTriggerHandler(c.Jobs, c.Chains, c.Triggers, c.Cron, c.Tenant, c.Uow, c.Clock);

        var view = await handler.HandleAsync(
            new CreateTriggerCommand(job.Id, "nightly", "Schedule", "0 0 * * *", null));

        Assert.Equal("Schedule", view.Kind);
        Assert.Equal(job.ProjectId, view.ProjectId);
        Assert.Equal(T0 + c.Cron.Interval, view.NextRunAt);
        Assert.True(view.Enabled);
    }

    [Fact]
    public async Task CreateTrigger_schedule_rejects_invalid_cron()
    {
        var c = new Ctx();
        var job = c.SeedJob();
        c.Cron.ValidOverride = false;
        var handler = new CreateTriggerHandler(c.Jobs, c.Chains, c.Triggers, c.Cron, c.Tenant, c.Uow, c.Clock);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.HandleAsync(new CreateTriggerCommand(job.Id, "x", "Schedule", "nonsense", null)));
    }

    [Fact]
    public async Task CreateTrigger_event_subscribes_to_name()
    {
        var c = new Ctx();
        var job = c.SeedJob();
        var handler = new CreateTriggerHandler(c.Jobs, c.Chains, c.Triggers, c.Cron, c.Tenant, c.Uow, c.Clock);

        var view = await handler.HandleAsync(
            new CreateTriggerCommand(job.Id, "on-deploy", "Event", null, "deploy.finished"));

        Assert.Equal("Event", view.Kind);
        Assert.Equal("deploy.finished", view.EventName);
        Assert.Null(view.NextRunAt);
    }

    // ── CreateLaunchpad ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateTrigger_launchpad_links_chain_table_and_prompt()
    {
        var c = new Ctx();
        var chain = JobChain.Create(Guid.NewGuid(), "enrich", null, new[] { Guid.NewGuid() }, T0);
        await c.Chains.AddAsync(chain);
        var handler = new CreateTriggerHandler(c.Jobs, c.Chains, c.Triggers, c.Cron, c.Tenant, c.Uow, c.Clock);

        var view = await handler.HandleAsync(new CreateTriggerCommand(
            null, "daily-enrich", "Launchpad", "0 6 * * *", null,
            chain.Id, "customers", "For each row, run the chain"));

        Assert.Equal("Launchpad", view.Kind);
        Assert.Null(view.JobId);
        Assert.Equal(chain.Id, view.ChainId);
        Assert.Equal("customers", view.SourceTable);
        Assert.Equal("For each row, run the chain", view.Prompt);
        Assert.Equal(chain.ProjectId, view.ProjectId);
        Assert.Equal(T0 + c.Cron.Interval, view.NextRunAt);
    }

    [Fact]
    public async Task CreateTrigger_launchpad_requires_prompt()
    {
        var c = new Ctx();
        var chain = JobChain.Create(Guid.NewGuid(), "enrich", null, new[] { Guid.NewGuid() }, T0);
        await c.Chains.AddAsync(chain);
        var handler = new CreateTriggerHandler(c.Jobs, c.Chains, c.Triggers, c.Cron, c.Tenant, c.Uow, c.Clock);

        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(
            new CreateTriggerCommand(null, "x", "Launchpad", "0 6 * * *", null, chain.Id, null, "")));
    }

    [Fact]
    public async Task CreateTrigger_launchpad_requires_existing_chain()
    {
        var c = new Ctx();
        var handler = new CreateTriggerHandler(c.Jobs, c.Chains, c.Triggers, c.Cron, c.Tenant, c.Uow, c.Clock);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            new CreateTriggerCommand(null, "x", "Launchpad", "0 6 * * *", null, Guid.NewGuid(), null, "do it")));
    }

    // ── Event fan-out ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EmitEvent_fans_out_to_subscribed_trigger_and_enqueues_run()
    {
        var c = new Ctx();
        var job = c.SeedJob();
        var trigger = JobTrigger.CreateEvent(job.ProjectId, job.Id, "on-deploy", "deploy.finished", T0);
        await c.Triggers.AddAsync(trigger);

        var view = await c.Dispatch().EmitAsync("deploy.finished", job.ProjectId, "{\"env\":\"prod\"}");

        Assert.Equal(1, view.TriggeredRuns);
        var queued = Assert.Single(c.Queue.Enqueued);
        Assert.Equal(job.Id, queued.JobId);
        Assert.Equal(TenantId, queued.TenantId);
        Assert.Equal("{\"env\":\"prod\"}", queued.Payload); // payload injected into the run
        Assert.Equal(T0, trigger.LastFiredAt);
        Assert.Single(c.Events.Occurrences);
    }

    [Fact]
    public async Task EmitEvent_does_not_fire_disabled_or_unmatched_triggers()
    {
        var c = new Ctx();
        var job = c.SeedJob();
        var disabled = JobTrigger.CreateEvent(job.ProjectId, job.Id, "off", "deploy.finished", T0);
        disabled.Disable(T0);
        var other = JobTrigger.CreateEvent(job.ProjectId, job.Id, "other", "something.else", T0);
        await c.Triggers.AddAsync(disabled);
        await c.Triggers.AddAsync(other);

        var view = await c.Dispatch().EmitAsync("deploy.finished", null, null);

        Assert.Equal(0, view.TriggeredRuns);
        Assert.Empty(c.Queue.Enqueued);
    }

    // ── Schedule scanning ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ScheduleScan_fires_due_trigger_and_advances_next_run()
    {
        var c = new Ctx();
        var job = c.SeedJob();
        // Due: next run in the past.
        var trigger = JobTrigger.CreateSchedule(job.ProjectId, job.Id, "nightly", "0 0 * * *", T0.AddMinutes(-1), T0);
        await c.Triggers.AddAsync(trigger);
        var scanner = new ScheduleScanService(c.Triggers, c.Queue, c.Cron, c.Tenant, c.Uow, c.Clock);

        var fired = await scanner.FireDueAsync();

        Assert.Equal(1, fired);
        Assert.Single(c.Queue.Enqueued);
        Assert.Equal(T0, trigger.LastFiredAt);
        Assert.Equal(T0 + c.Cron.Interval, trigger.NextRunAt); // advanced past now
    }

    [Fact]
    public async Task ScheduleScan_skips_when_nothing_due()
    {
        var c = new Ctx();
        var job = c.SeedJob();
        var trigger = JobTrigger.CreateSchedule(job.ProjectId, job.Id, "future", "0 0 * * *", T0.AddHours(1), T0);
        await c.Triggers.AddAsync(trigger);
        var scanner = new ScheduleScanService(c.Triggers, c.Queue, c.Cron, c.Tenant, c.Uow, c.Clock);

        var fired = await scanner.FireDueAsync();

        Assert.Equal(0, fired);
        Assert.Empty(c.Queue.Enqueued);
    }

    // ── Event types ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DefineEventType_then_list_includes_builtins_and_user_event()
    {
        var c = new Ctx();
        var define = new DefineEventTypeHandler(c.Events, c.Uow, c.Clock);
        await define.HandleAsync(new DefineEventTypeCommand("deploy.finished", "after a deploy", null));

        var list = new ListEventTypesHandler(c.Events);
        var types = await list.HandleAsync(new ListEventTypesQuery());

        Assert.Contains(types, t => t.Name == "deploy.finished" && !t.IsBuiltIn);
        Assert.Contains(types, t => t.Name == BuiltInEvents.JobCompleted && t.IsBuiltIn);
    }

    [Fact]
    public async Task DefineEventType_rejects_builtin_name()
    {
        var c = new Ctx();
        var define = new DefineEventTypeHandler(c.Events, c.Uow, c.Clock);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            define.HandleAsync(new DefineEventTypeCommand(BuiltInEvents.JobCompleted, null, null)));
    }
}
