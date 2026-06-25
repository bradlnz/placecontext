using PlaceContext.Application.Features;
using PlaceContext.Domain.Entities;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

/// <summary>
/// Tests semantic search over job-run outputs: the query is embedded and the nearest stored run
/// outputs are returned by cosine similarity; disabled embeddings yield no results.
/// </summary>
public class RunEmbeddingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Search_returns_nearest_output_by_similarity()
    {
        var projectId = Guid.NewGuid();
        var gateway = new FakeEmbeddingGateway(dimensions: 3);
        var repo = new InMemoryRunEmbeddingRepository();

        repo.Store.Add(RunEmbedding.Create(Guid.NewGuid(), Guid.NewGuid(), projectId, "about cats", new[] { 1f, 0f, 0f }, T0));
        repo.Store.Add(RunEmbedding.Create(Guid.NewGuid(), Guid.NewGuid(), projectId, "about dogs", new[] { 0f, 1f, 0f }, T0));
        gateway.Set("feline question", new[] { 0.9f, 0.1f, 0f }); // closest to "about cats"

        var handler = new SearchRunOutputsHandler(gateway, repo);
        var results = await handler.HandleAsync(new SearchRunOutputsQuery(projectId, "feline question", 5));

        Assert.Equal("about cats", results[0].Text);
        Assert.True(results[0].Score > results[1].Score);
    }

    [Fact]
    public async Task Search_is_scoped_to_the_project()
    {
        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();
        var gateway = new FakeEmbeddingGateway(dimensions: 3);
        var repo = new InMemoryRunEmbeddingRepository();
        repo.Store.Add(RunEmbedding.Create(Guid.NewGuid(), Guid.NewGuid(), projectB, "other project", new[] { 1f, 0f, 0f }, T0));
        gateway.Set("q", new[] { 1f, 0f, 0f });

        var handler = new SearchRunOutputsHandler(gateway, repo);
        var results = await handler.HandleAsync(new SearchRunOutputsQuery(projectA, "q", 5));

        Assert.Empty(results); // the only embedding belongs to a different project
    }

    [Fact]
    public async Task Search_returns_empty_when_embeddings_disabled()
    {
        var handler = new SearchRunOutputsHandler(new FakeEmbeddingGateway(enabled: false), new InMemoryRunEmbeddingRepository());
        var results = await handler.HandleAsync(new SearchRunOutputsQuery(Guid.NewGuid(), "anything"));
        Assert.Empty(results);
    }

    [Fact]
    public async Task RunJob_embeds_organized_output_when_enabled()
    {
        var jobs = new InMemoryJobRepository();
        var runs = new InMemoryJobRunRepository();
        var contexts = new InMemoryProjectContextRepository();
        var runner = new FakeWorkloadRunner();
        var uow = new RecordingUnitOfWork();
        var clock = new FakeClock(T0);
        var llm = new FakeLlmGateway(enabled: true);
        var gateway = new FakeEmbeddingGateway(dimensions: 3);
        var store = new InMemoryRunEmbeddingRepository();

        var map = new PlaceContext.Domain.ValueObjects.MapSpec("img/worker:latest", new[] { "{}" }, new Dictionary<string, string>());
        var job = Job.Create(Guid.NewGuid(), "j", null, map, null, 1, PlaceContext.Domain.ValueObjects.ExitCodePolicy.Default, T0);
        await jobs.AddAsync(job);

        var handler = new RunJobHandler(jobs, runs, contexts, runner, uow, clock,
            events: null, llm: llm, embeddings: gateway, embeddingStore: store);
        await handler.HandleAsync(new RunJobCommand(job.Id));

        // The organized output was embedded (unknown text → zero vector is still length>0 here),
        // so one embedding is stored, linked to the run.
        var stored = Assert.Single(store.Store);
        Assert.Equal(job.Id, stored.JobId);
        Assert.Equal(job.ProjectId, stored.ProjectId);
    }
}
