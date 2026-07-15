using PlaceContext.Application.Features;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

/// <summary>
/// Application-layer tests for the handlers added to back the /api/v1 management API: get-by-id reads
/// for projects and triggers, and job deletion. Unit tests over in-memory fakes.
/// </summary>
public class ManagementApiHandlerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    // ── GetProjectById ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetProjectById_returns_the_matching_project()
    {
        var repo = new InMemoryProjectRepository();
        var project = Project.Discover(ProjectPath.From("/home/brad/code/alpha"), ProjectName.From("alpha"), T0);
        project.Register(T0);
        await repo.AddAsync(project);

        var handler = new GetProjectByIdHandler(repo);
        var view = await handler.HandleAsync(new GetProjectByIdQuery(project.Id.Value));

        Assert.NotNull(view);
        Assert.Equal("alpha", view!.Name);
        Assert.Equal(project.Id.Value, view.Id);
    }

    [Fact]
    public async Task GetProjectById_returns_null_for_an_unknown_id()
    {
        var handler = new GetProjectByIdHandler(new InMemoryProjectRepository());
        var view = await handler.HandleAsync(new GetProjectByIdQuery(Guid.NewGuid()));
        Assert.Null(view);
    }

    [Fact]
    public async Task GetProjectById_returns_null_for_an_empty_guid_rather_than_throwing()
    {
        // ProjectId.From rejects Guid.Empty — the handler must treat that as "not found", not blow up
        // into a 500 for a caller that (mis)requests /api/v1/projects/00000000-0000-0000-0000-000000000000.
        var handler = new GetProjectByIdHandler(new InMemoryProjectRepository());
        var view = await handler.HandleAsync(new GetProjectByIdQuery(Guid.Empty));
        Assert.Null(view);
    }

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
