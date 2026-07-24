using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using Xunit;

namespace PlaceContext.Domain.Tests;

/// <summary>
/// Domain tests for JobTrigger firing/scheduling behaviour, EventDefinition invariants, and
/// EventOccurrence sourcing. Pure in-process — no I/O.
/// </summary>
public class TriggerAndEventTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid JobId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    // ---- JobTrigger: schedule ----

    [Fact]
    public void CreateSchedule_is_enabled_with_cron_and_next_run()
    {
        var next = T0.AddHours(1);
        var t = JobTrigger.CreateSchedule(ProjectId, JobId, "nightly", "0 0 * * *", next, T0);

        Assert.Equal(TriggerKind.Schedule, t.Kind);
        Assert.True(t.Enabled);
        Assert.Equal("0 0 * * *", t.CronExpression);
        Assert.Null(t.EventName);
        Assert.Equal(next, t.NextRunAt);
        Assert.Null(t.LastFiredAt);
    }

    // ---- JobTrigger: launchpad ----

    [Fact]
    public void CreateLaunchpad_is_enabled_cron_based_and_jobless()
    {
        var next = T0.AddHours(1);
        var chainId = Guid.NewGuid();
        var t = JobTrigger.CreateLaunchpad(ProjectId, "daily-enrich", "0 6 * * *", chainId,
            "customers", "For each row, run the chain", next, T0);

        Assert.Equal(TriggerKind.Launchpad, t.Kind);
        Assert.True(t.Enabled);
        Assert.Null(t.JobId);
        Assert.Equal(chainId, t.ChainId);
        Assert.Equal("customers", t.SourceTable);
        Assert.Equal("For each row, run the chain", t.Prompt);
        Assert.Equal("0 6 * * *", t.CronExpression);
        Assert.Equal(next, t.NextRunAt);
    }

    [Fact]
    public void CreateLaunchpad_validates_required_fields()
    {
        Assert.Throws<ArgumentException>(() =>
            JobTrigger.CreateLaunchpad(ProjectId, "x", "0 6 * * *", Guid.Empty, null, "do it", T0, T0));
        Assert.Throws<ArgumentException>(() =>
            JobTrigger.CreateLaunchpad(ProjectId, "x", "  ", Guid.NewGuid(), null, "do it", T0, T0));
        Assert.Throws<ArgumentException>(() =>
            JobTrigger.CreateLaunchpad(ProjectId, "x", "0 6 * * *", Guid.NewGuid(), null, " ", T0, T0));
    }

    [Fact]
    public void Launchpad_is_due_and_mark_fired_advances_next_run()
    {
        var t = JobTrigger.CreateLaunchpad(ProjectId, "x", "* * * * *", Guid.NewGuid(),
            null, "do it", T0.AddMinutes(-1), T0);
        Assert.True(t.IsDue(T0));

        var next = T0.AddMinutes(1);
        t.MarkFired(T0, next);
        Assert.Equal(T0, t.LastFiredAt);
        Assert.Equal(next, t.NextRunAt);
        Assert.False(t.IsDue(T0));
    }

    [Fact]
    public void Launchpad_disable_clears_next_run_enable_reschedules()
    {
        var t = JobTrigger.CreateLaunchpad(ProjectId, "x", "* * * * *", Guid.NewGuid(),
            null, "do it", T0, T0);
        t.Disable(T0);
        Assert.Null(t.NextRunAt);
        Assert.False(t.IsDue(T0));

        var next = T0.AddMinutes(5);
        t.Enable(next, T0);
        Assert.True(t.Enabled);
        Assert.Equal(next, t.NextRunAt);
    }

    [Fact]
    public void Launchpad_can_be_rescheduled()
    {
        var t = JobTrigger.CreateLaunchpad(ProjectId, "x", "* * * * *", Guid.NewGuid(),
            null, "do it", T0, T0);
        var next = T0.AddHours(2);
        t.Reschedule("0 */2 * * *", next, T0);
        Assert.Equal("0 */2 * * *", t.CronExpression);
        Assert.Equal(next, t.NextRunAt);
    }

    [Fact]
    public void CreateSchedule_rejects_empty_cron()
    {
        Assert.Throws<ArgumentException>(() =>
            JobTrigger.CreateSchedule(ProjectId, JobId, "x", "   ", T0, T0));
    }

    [Fact]
    public void Schedule_is_due_when_next_run_in_the_past_and_enabled()
    {
        var t = JobTrigger.CreateSchedule(ProjectId, JobId, "x", "* * * * *", T0.AddMinutes(-1), T0);
        Assert.True(t.IsDue(T0));
    }

    [Fact]
    public void Schedule_is_not_due_when_next_run_in_the_future()
    {
        var t = JobTrigger.CreateSchedule(ProjectId, JobId, "x", "* * * * *", T0.AddMinutes(5), T0);
        Assert.False(t.IsDue(T0));
    }

    [Fact]
    public void Disabled_schedule_is_never_due()
    {
        var t = JobTrigger.CreateSchedule(ProjectId, JobId, "x", "* * * * *", T0.AddMinutes(-1), T0);
        t.Disable(T0);
        Assert.False(t.IsDue(T0));
        Assert.Null(t.NextRunAt);
    }

    [Fact]
    public void MarkFired_advances_next_run_and_records_last_fired()
    {
        var t = JobTrigger.CreateSchedule(ProjectId, JobId, "x", "* * * * *", T0, T0);
        var nextNext = T0.AddMinutes(1);

        t.MarkFired(T0, nextNext);

        Assert.Equal(T0, t.LastFiredAt);
        Assert.Equal(nextNext, t.NextRunAt);
    }

    [Fact]
    public void Reschedule_updates_cron_and_next_run()
    {
        var t = JobTrigger.CreateSchedule(ProjectId, JobId, "x", "* * * * *", T0, T0);
        t.Reschedule("0 9 * * *", T0.AddHours(9), T0.AddMinutes(1));

        Assert.Equal("0 9 * * *", t.CronExpression);
        Assert.Equal(T0.AddHours(9), t.NextRunAt);
    }

    [Fact]
    public void Reschedule_on_event_trigger_throws()
    {
        var t = JobTrigger.CreateEvent(ProjectId, JobId, "x", "deploy.done", T0);
        Assert.Throws<InvalidOperationException>(() => t.Reschedule("* * * * *", T0, T0));
    }

    [Fact]
    public void Enable_restores_next_run_for_schedule()
    {
        var t = JobTrigger.CreateSchedule(ProjectId, JobId, "x", "* * * * *", T0, T0);
        t.Disable(T0);
        var next = T0.AddMinutes(2);
        t.Enable(next, T0.AddMinutes(1));

        Assert.True(t.Enabled);
        Assert.Equal(next, t.NextRunAt);
    }

    // ---- JobTrigger: event ----

    [Fact]
    public void CreateEvent_subscribes_to_event_name()
    {
        var t = JobTrigger.CreateEvent(ProjectId, JobId, "on-deploy", "deploy.finished", T0);

        Assert.Equal(TriggerKind.Event, t.Kind);
        Assert.Equal("deploy.finished", t.EventName);
        Assert.Null(t.CronExpression);
        Assert.Null(t.NextRunAt);
    }

    [Fact]
    public void Event_trigger_matches_subscribed_name_case_insensitively()
    {
        var t = JobTrigger.CreateEvent(ProjectId, JobId, "x", "Deploy.Finished", T0);
        Assert.True(t.MatchesEvent("deploy.finished"));
        Assert.False(t.MatchesEvent("other.event"));
    }

    [Fact]
    public void Disabled_event_trigger_does_not_match()
    {
        var t = JobTrigger.CreateEvent(ProjectId, JobId, "x", "deploy.finished", T0);
        t.Disable(T0);
        Assert.False(t.MatchesEvent("deploy.finished"));
    }

    [Fact]
    public void CreateEvent_rejects_empty_event_name()
    {
        Assert.Throws<ArgumentException>(() =>
            JobTrigger.CreateEvent(ProjectId, JobId, "x", "  ", T0));
    }

    [Fact]
    public void CreateSchedule_rejects_empty_job_id()
    {
        Assert.Throws<ArgumentException>(() =>
            JobTrigger.CreateSchedule(ProjectId, Guid.Empty, "x", "* * * * *", T0, T0));
    }

    // ---- EventDefinition ----

    [Fact]
    public void EventDefinition_Create_trims_and_stores()
    {
        var d = EventDefinition.Create("  deploy.finished  ", " when a deploy ends ", null, T0);
        Assert.Equal("deploy.finished", d.Name);
        Assert.Equal("when a deploy ends", d.Description);
    }

    [Fact]
    public void EventDefinition_Create_rejects_builtin_name()
    {
        Assert.Throws<ArgumentException>(() =>
            EventDefinition.Create(BuiltInEvents.JobCompleted, null, null, T0));
    }

    [Fact]
    public void EventDefinition_Create_rejects_empty_name()
    {
        Assert.Throws<ArgumentException>(() => EventDefinition.Create("   ", null, null, T0));
    }

    [Fact]
    public void BuiltInEvents_recognises_reserved_names_case_insensitively()
    {
        Assert.True(BuiltInEvents.IsBuiltIn("Activity.Recorded"));
        Assert.False(BuiltInEvents.IsBuiltIn("deploy.finished"));
    }

    // ---- EventOccurrence ----

    [Fact]
    public void Emit_marks_source_user()
    {
        var o = EventOccurrence.Emit("deploy.finished", ProjectId, "{\"ok\":true}", T0);
        Assert.Equal(EventSource.User, o.Source);
        Assert.Equal(ProjectId, o.ProjectId);
        Assert.Equal("deploy.finished", o.Name);
    }

    [Fact]
    public void Raise_marks_source_domain()
    {
        var o = EventOccurrence.Raise(BuiltInEvents.JobCompleted, ProjectId, null, T0);
        Assert.Equal(EventSource.Domain, o.Source);
    }

    [Fact]
    public void Emit_rejects_empty_name()
    {
        Assert.Throws<ArgumentException>(() => EventOccurrence.Emit("  ", null, null, T0));
    }
}
