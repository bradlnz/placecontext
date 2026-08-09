using PlaceContext.Application.Features;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

/// <summary>
/// Application-layer tests for Jobs-owned management handlers. Unit tests over in-memory fakes.
/// </summary>
public class ManagementApiHandlerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);


    // ── DeleteJob ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteJob_removes_an_existing_job_and_returns_true()
    {
        var jobs = new InMemoryJobRepository();
        var uow = new RecordingUnitOfWork();
        var map = new MapSpec("img/worker:latest", new[] { "{}" }, new Dictionary<string, string>());
        var job = Job.Create(Guid.NewGuid(), "nightly-report", null, map, null, 1, ExitCodePolicy.Default, T0);
        await jobs.AddAsync(job);

        var handler = new DeleteJobHandler(jobs, uow);
        var deleted = await handler.HandleAsync(new DeleteJobCommand(job.Id));

        Assert.True(deleted);
        Assert.Equal(1, uow.SaveCount);
        Assert.Null(await jobs.GetByIdAsync(job.Id));
    }

    [Fact]
    public async Task DeleteJob_returns_false_and_does_not_save_for_an_unknown_id()
    {
        var uow = new RecordingUnitOfWork();
        var handler = new DeleteJobHandler(new InMemoryJobRepository(), uow);

        var deleted = await handler.HandleAsync(new DeleteJobCommand(Guid.NewGuid()));

        Assert.False(deleted);
        Assert.Equal(0, uow.SaveCount);
    }

    // ── GetTriggerById ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTriggerById_returns_the_matching_trigger()
    {
        var triggers = new InMemoryJobTriggerRepository();
        var trigger = JobTrigger.CreateSchedule(Guid.NewGuid(), Guid.NewGuid(), "nightly", "0 0 * * *", T0.AddHours(1), T0);
        await triggers.AddAsync(trigger);

        var handler = new GetTriggerByIdHandler(triggers);
        var view = await handler.HandleAsync(new GetTriggerByIdQuery(trigger.Id));

        Assert.NotNull(view);
        Assert.Equal("nightly", view!.Name);
        Assert.Equal("Schedule", view.Kind);
    }

    [Fact]
    public async Task GetTriggerById_returns_null_for_an_unknown_id()
    {
        var handler = new GetTriggerByIdHandler(new InMemoryJobTriggerRepository());
        var view = await handler.HandleAsync(new GetTriggerByIdQuery(Guid.NewGuid()));
        Assert.Null(view);
    }
}
