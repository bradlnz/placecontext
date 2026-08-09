using PlaceContext.Application.Features;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;

namespace PlaceContext.Jobs.Tests;

public sealed class RunJobDataBoundaryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Completed_run_is_sent_to_Data_service_boundary()
    {
        var jobs = new InMemoryJobRepository();
        var runs = new InMemoryJobRunRepository();
        var data = new CapturingDataClient();
        var job = Job.Create(
            Guid.NewGuid(),
            "nightly-etl",
            null,
            new MapSpec("img/worker:latest", ["{}"], new Dictionary<string, string>()),
            null,
            1,
            ExitCodePolicy.Default,
            T0);
        await jobs.AddAsync(job);

        var handler = new RunJobHandler(
            jobs,
            runs,
            new FakeWorkloadRunner(),
            new RecordingUnitOfWork(),
            new FakeClock(T0),
            dataClient: data);

        var result = await handler.HandleAsync(new RunJobCommand(job.Id));

        Assert.Equal(result.Id, data.Run?.Id);
        Assert.Same(job, data.Job);
    }

    private sealed class CapturingDataClient : PlaceContext.Jobs.Integration.IJobDataClient
    {
        public Job? Job { get; private set; }
        public JobRun? Run { get; private set; }

        public Task ProcessJobResultAsync(
            Job job,
            JobRun run,
            CancellationToken cancellationToken = default)
        {
            Job = job;
            Run = run;
            return Task.CompletedTask;
        }

        public Task ProcessChainResultAsync(
            Guid chainId,
            Guid chainRunId,
            Guid projectId,
            string? primaryOutput,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
