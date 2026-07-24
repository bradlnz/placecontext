using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests.Agents;

public class AgentContextBuilderTests
{
    [Fact]
    public async Task BuildContext_includes_related_project_data_when_indexer_has_hits()
    {
        var indexer = new FakeContentIndexer
        {
            HitsToReturn = new[]
            {
                new ContentSearchHit(ContentKind.ProjectData, "spec:auth", "Auth module uses JWT tokens.", 0.9, DateTimeOffset.UtcNow),
                new ContentSearchHit(ContentKind.ProjectData, "spec:billing", "Billing runs nightly.", 0.8, DateTimeOffset.UtcNow),
            },
        };
        var builder = new AgentContextBuilder(contentIndexer: indexer);

        var context = await builder.BuildContextAsync(Guid.NewGuid(), "how does auth work?", maxChunks: 5);

        Assert.Contains("## Related project data (semantically relevant)", context);
        Assert.Contains("spec:auth", context);
        Assert.Contains("Auth module uses JWT tokens.", context);
        Assert.Contains("spec:billing", context);
        // Project data only — run outputs are covered by the run-embedding search.
        Assert.Equal(ContentKind.ProjectData, indexer.LastSearchKind);
    }

    [Fact]
    public async Task BuildContext_omits_related_project_data_when_indexer_has_no_hits()
    {
        var builder = new AgentContextBuilder(contentIndexer: new FakeContentIndexer());

        var context = await builder.BuildContextAsync(Guid.NewGuid(), "anything", maxChunks: 5);

        Assert.DoesNotContain("Related project data", context);
    }

    [Fact]
    public async Task BuildContext_omits_related_project_data_when_indexer_disabled()
    {
        var indexer = new FakeContentIndexer
        {
            IsEnabled = false,
            HitsToReturn = new[]
            {
                new ContentSearchHit(ContentKind.ProjectData, "spec:auth", "Auth module uses JWT tokens.", 0.9, DateTimeOffset.UtcNow),
            },
        };
        var builder = new AgentContextBuilder(contentIndexer: indexer);

        var context = await builder.BuildContextAsync(Guid.NewGuid(), "how does auth work?", maxChunks: 5);

        Assert.DoesNotContain("Related project data", context);
    }

    [Fact]
    public async Task BuildContext_swallows_indexer_failures()
    {
        var builder = new AgentContextBuilder(contentIndexer: new ThrowingContentIndexer());

        var context = await builder.BuildContextAsync(Guid.NewGuid(), "boom", maxChunks: 5);

        Assert.Equal(string.Empty, context);
    }

    private sealed class ThrowingContentIndexer : IContentIndexer
    {
        public bool IsEnabled => true;
        public Task IndexAsync(Guid projectId, string kind, string sourceKey, string text, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task IndexManyAsync(Guid projectId, string kind, IReadOnlyList<(string SourceKey, string Text)> items, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<IReadOnlyList<ContentSearchHit>> SearchAsync(Guid projectId, string query, int take = 10, string? kind = null, CancellationToken ct = default)
            => throw new InvalidOperationException("index unavailable");
    }
}
