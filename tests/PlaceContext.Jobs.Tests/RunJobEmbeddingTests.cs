using PlaceContext.Application.Features;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Jobs.Integration;
using PlaceContext.TestSupport;

namespace PlaceContext.Jobs.Tests;

public sealed class RunJobEmbeddingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunJob_sends_organized_output_to_Search()
    {
        var jobs = new InMemoryJobRepository();
        var runs = new InMemoryJobRunRepository();
        var search = new CapturingSearchClient();
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
            searchClient: search);

        var result = await handler.HandleAsync(new RunJobCommand(job.Id));

        Assert.Equal(result.Id, search.RunId);
        Assert.Equal(job.Id, search.JobId);
        Assert.Equal(job.ProjectId, search.ProjectId);
        Assert.Contains("Job run: nightly-etl", search.Text);
    }

    private sealed class CapturingSearchClient : IJobSearchClient
    {
        public Guid RunId { get; private set; }
        public Guid JobId { get; private set; }
        public Guid ProjectId { get; private set; }
        public string Text { get; private set; } = string.Empty;

        public Task IndexRunOutputAsync(
            Guid runId,
            Guid jobId,
            Guid projectId,
            string text,
            CancellationToken cancellationToken = default)
        {
            RunId = runId;
            JobId = jobId;
            ProjectId = projectId;
            Text = text;
            return Task.CompletedTask;
        }
    }
}
