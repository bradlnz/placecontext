using PlaceContext.Application.Features;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;

namespace PlaceContext.Application.Tests;

public sealed class RunJobEmbeddingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunJob_embeds_organized_output_when_enabled()
    {
        var jobs = new InMemoryJobRepository();
        var runs = new InMemoryJobRunRepository();
        var runner = new FakeWorkloadRunner();
        var unitOfWork = new RecordingUnitOfWork();
        var gateway = new FakeEmbeddingGateway(dimensions: 3);
        var store = new InMemoryRunEmbeddingRepository();

        var map = new MapSpec(
            "img/worker:latest", new[] { "{}" }, new Dictionary<string, string>());
        var job = Job.Create(
            Guid.NewGuid(), "j", null, map, null, 1, ExitCodePolicy.Default, T0);
        await jobs.AddAsync(job);

        var handler = new RunJobHandler(
            jobs, runs, runner, unitOfWork, new FakeClock(T0),
            events: null, embeddings: gateway, embeddingStore: store);
        await handler.HandleAsync(new RunJobCommand(job.Id));

        var stored = Assert.Single(store.Store);
        Assert.Equal(job.Id, stored.JobId);
        Assert.Equal(job.ProjectId, stored.ProjectId);
    }
}
