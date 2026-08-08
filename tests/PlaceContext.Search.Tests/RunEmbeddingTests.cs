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

}
