using PlaceContext.Search.Contracts.Commands;
using PlaceContext.Search.Handlers.Workspace;
using PlaceContext.TestSupport;

namespace PlaceContext.Search.Tests;

public sealed class IndexRunOutputHandlerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 9, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Enabled_embeddings_are_stored_by_Search()
    {
        var embeddings = new FakeEmbeddingGateway(dimensions: 3);
        embeddings.Set("organized output", [0.1f, 0.2f, 0.3f]);
        var repository = new InMemoryRunEmbeddingRepository();
        var command = new IndexRunOutputCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "organized output");

        var indexed = await new IndexRunOutputHandler(
            embeddings,
            repository,
            new FakeClock(T0)).HandleAsync(command);

        Assert.True(indexed);
        var stored = Assert.Single(repository.Store);
        Assert.Equal(command.RunId, stored.JobRunId);
        Assert.Equal(command.JobId, stored.JobId);
        Assert.Equal(command.ProjectId, stored.ProjectId);
        Assert.Equal(command.Text, stored.Text);
        Assert.Equal(T0, stored.CreatedAt);
    }

    [Fact]
    public async Task Disabled_embeddings_are_a_no_op()
    {
        var repository = new InMemoryRunEmbeddingRepository();
        var command = new IndexRunOutputCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "organized output");

        var indexed = await new IndexRunOutputHandler(
            new FakeEmbeddingGateway(enabled: false),
            repository,
            new FakeClock(T0)).HandleAsync(command);

        Assert.False(indexed);
        Assert.Empty(repository.Store);
    }
}
