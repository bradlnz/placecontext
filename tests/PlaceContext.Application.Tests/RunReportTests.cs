using PlaceContext.Application.Features;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

/// <summary>
/// The reporting feed (ListRecentRunReports): recent runs across every job, newest first, each
/// carrying its artifacts and the owning job's name — the source the portal charts globally.
/// </summary>
public class RunReportTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 8, 10, 0, 0, TimeSpan.Zero);

    private static WorkloadSnapshot Snapshot()
    {
        var mapSpec = new MapSpec("img", new[] { "{}" }, new Dictionary<string, string>());
        return WorkloadSnapshot.From(mapSpec, null, 1);
    }

    private static Job MakeJob(string name)
        => Job.Create(Guid.NewGuid(), name, null, new MapSpec("img", new[] { "{}" }, new Dictionary<string, string>()),
            null, 1, new ExitCodePolicy(new[] { 0 }, Array.Empty<int>()), T0);

    [Fact]
    public async Task Recent_runs_come_back_newest_first_with_their_job_names()
    {
        var jobs = new InMemoryJobRepository();
        var runs = new InMemoryJobRunRepository();
        var older = MakeJob("older-job");
        var newer = MakeJob("newer-job");
        await jobs.AddAsync(older);
        await jobs.AddAsync(newer);
        await runs.AddAsync(JobRun.Start(older.Id, older.ProjectId, T0, Snapshot()));
        await runs.AddAsync(JobRun.Start(newer.Id, newer.ProjectId, T0.AddMinutes(5), Snapshot()));

        var handler = new ListRecentRunReportsHandler(runs, jobs);
        var reports = await handler.HandleAsync(new ListRecentRunReportsQuery(Take: 10));

        Assert.Equal(2, reports.Count);
        Assert.Equal("newer-job", reports[0].JobName);
        Assert.Equal("older-job", reports[1].JobName);
        Assert.Equal(newer.ProjectId, reports[0].Run.ProjectId);
    }

    [Fact]
    public async Task Take_caps_the_feed_and_a_deleted_job_still_reports()
    {
        var jobs = new InMemoryJobRepository();
        var runs = new InMemoryJobRunRepository();
        var job = MakeJob("kept-job");
        await jobs.AddAsync(job);
        for (var i = 0; i < 5; i++)
            await runs.AddAsync(JobRun.Start(job.Id, job.ProjectId, T0.AddMinutes(i), Snapshot()));
        await runs.AddAsync(JobRun.Start(Guid.NewGuid(), Guid.NewGuid(), T0.AddMinutes(9), Snapshot())); // orphan

        var handler = new ListRecentRunReportsHandler(runs, jobs);
        var reports = await handler.HandleAsync(new ListRecentRunReportsQuery(Take: 3));

        Assert.Equal(3, reports.Count);
        Assert.Equal("(deleted job)", reports[0].JobName); // the orphan is newest
    }
}
