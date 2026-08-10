using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
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

        Assert.Contains("## Related project content (semantically relevant)", context);
        Assert.Contains("spec:auth", context);
        Assert.Contains("Auth module uses JWT tokens.", context);
        Assert.Contains("spec:billing", context);
        // Search all kinds so documents, decisions, activity, etc. are all in scope.
        Assert.Null(indexer.LastSearchKind);
    }

    [Fact]
    public async Task BuildContext_omits_related_project_data_when_indexer_has_no_hits()
    {
        var builder = new AgentContextBuilder(contentIndexer: new FakeContentIndexer());

        var context = await builder.BuildContextAsync(Guid.NewGuid(), "anything", maxChunks: 5);

        Assert.DoesNotContain("Related project content", context);
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

        Assert.DoesNotContain("Related project content", context);
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

    // ── Direct mention lookup (addresses / quoted names → artifacts + records) ──

    [Theory]
    [InlineData("Tell me about 20 Balfour Street, Darra", "20 Balfour Street")]
    [InlineData("what do we know about 123A Old Windsor Road?", "123A Old Windsor Road")]
    [InlineData("summarise 5 McGregor Crescent and 9 McGregor Crescent", "5 McGregor Crescent")]
    public void ExtractMentionTerms_finds_street_addresses(string message, string expected)
        => Assert.Contains(expected, AgentContextBuilder.ExtractMentionTerms(message));

    [Fact]
    public void ExtractMentionTerms_finds_quoted_names()
        => Assert.Contains("Balfour feasibility study", AgentContextBuilder.ExtractMentionTerms("read \"Balfour feasibility study\" please"));

    [Fact]
    public void ExtractMentionTerms_ignores_plain_questions()
        => Assert.Empty(AgentContextBuilder.ExtractMentionTerms("how does the feasibility matrix work?"));

    [Fact]
    public async Task BuildContext_includes_matching_artifacts_for_an_address()
    {
        var link = RunArtifactLink.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            PostJobActionKind.HtmlReport, "Feasibility — 20 Balfour Street, Darra", "bucket", "runs/x/report.html",
            "text/html", 1234, DateTimeOffset.UtcNow);
        var artifacts = new FakeArtifactLinkRepository { LinksToReturn = new[] { link } };
        var builder = new AgentContextBuilder(artifacts: artifacts);

        var context = await builder.BuildContextAsync(link.ProjectId, "Tell me about 20 Balfour Street, Darra", maxChunks: 5);

        Assert.Contains("## Direct matches in project data", context);
        Assert.Contains("Feasibility — 20 Balfour Street, Darra", context);
        Assert.Contains($"id:{link.Id}", context);
        Assert.Equal("20 Balfour Street", artifacts.LastSearch);
    }

    [Fact]
    public async Task BuildContext_includes_matching_entity_records_for_an_address()
    {
        var projectId = Guid.NewGuid();
        var entity = DataEntity.Create(projectId, "Sites", "feasibility_matrix", "address",
            Array.Empty<EntityRelation>(), DateTimeOffset.UtcNow);
        var store = new FakeProjectDataStore
        {
            PageToReturn = new ProjectTablePageResult(
                new[] { "address", "config", "margin_pct" },
                new[] { (IReadOnlyList<string?>)new string?[] { "20 Balfour Street, Darra", "1-into-2", "31.5" } },
                TotalCount: 1, Page: 1, PageSize: 3),
        };
        var builder = new AgentContextBuilder(
            entities: new FakeDataEntityRepository(entity), projectData: store);

        var context = await builder.BuildContextAsync(projectId, "Tell me about 20 Balfour Street, Darra", maxChunks: 5);

        Assert.Contains("## Direct matches in project data", context);
        Assert.Contains("**Sites** record:", context);
        Assert.Contains("address=20 Balfour Street, Darra", context);
        Assert.Equal("20 Balfour Street", store.LastSearch);
    }

    [Fact]
    public async Task BuildContext_skips_direct_matches_for_plain_questions()
    {
        var artifacts = new FakeArtifactLinkRepository();
        var builder = new AgentContextBuilder(artifacts: artifacts);

        var context = await builder.BuildContextAsync(Guid.NewGuid(), "how does the feasibility matrix work?", maxChunks: 5);

        Assert.Equal(string.Empty, context);
        Assert.Null(artifacts.LastSearch);
    }

    private sealed class FakeArtifactLinkRepository : IRunArtifactLinkRepository
    {
        public IReadOnlyList<RunArtifactLink> LinksToReturn { get; set; } = Array.Empty<RunArtifactLink>();
        public string? LastSearch { get; private set; }
        public Task AddAsync(RunArtifactLink link, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<RunArtifactLink>> ListForRunAsync(Guid runId, CancellationToken ct = default) => Task.FromResult(LinksToReturn);
        public Task<RunArtifactLink?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<RunArtifactLink?>(null);
        public Task<IReadOnlyList<RunArtifactLink>> ListForJobAsync(Guid jobId, CancellationToken ct = default) => Task.FromResult(LinksToReturn);
        public Task<IReadOnlyList<RunArtifactLink>> ListRecentAsync(int take, CancellationToken ct = default) => Task.FromResult(LinksToReturn);
        public Task<IReadOnlyList<RunArtifactLink>> ListForProjectAsync(Guid projectId, int take, string? search = null, CancellationToken ct = default)
        {
            LastSearch = search;
            return Task.FromResult(LinksToReturn);
        }
        public Task<IReadOnlyList<RunArtifactLink>> ListPendingOcrAsync(int take, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RunArtifactLink>>(Array.Empty<RunArtifactLink>());
        public Task MarkOcrProcessedAsync(Guid artifactId, DateTimeOffset processedAt, string? error, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeDataEntityRepository : IDataEntityRepository
    {
        private readonly IReadOnlyList<DataEntity> _entities;
        public FakeDataEntityRepository(params DataEntity[] entities) => _entities = entities;
        public Task AddAsync(DataEntity entity, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(DataEntity entity, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid entityId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<DataEntity?> GetByIdAsync(Guid entityId, CancellationToken ct = default) => Task.FromResult<DataEntity?>(null);
        public Task<IReadOnlyList<DataEntity>> ListForProjectAsync(Guid projectId, CancellationToken ct = default) => Task.FromResult(_entities);
    }

    private sealed class FakeProjectDataStore : IProjectDataStore
    {
        public ProjectTablePageResult PageToReturn { get; set; } = new(Array.Empty<string>(), Array.Empty<IReadOnlyList<string?>>(), 0, 1, 50);
        public string? LastSearch { get; private set; }
        public Task<ProjectQueryResult> ExecuteAsync(Guid projectId, string sql, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ProjectTablePageResult> QueryTablePageAsync(Guid projectId, string tableName, string? search,
            int page, int pageSize, string? sortColumn = null, bool sortDescending = false, CancellationToken ct = default)
        {
            LastSearch = search;
            return Task.FromResult(PageToReturn);
        }
        public Task<ProjectTableReadResult> ReadTableAsync(Guid projectId, string tableName, long maxRows = 10000, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ProjectTableInfo>> ListTablesAsync(Guid projectId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task CreateTableAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RenameTableAsync(Guid projectId, string from, string to, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProjectColumnInfo>> ListColumnsAsync(Guid projectId, string tableName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddColumnAsync(Guid projectId, string tableName, ProjectColumnSpec column, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DropColumnAsync(Guid projectId, string tableName, string columnName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DropTableAsync(Guid projectId, string tableName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> ExportTableCsvAsync(Guid projectId, string tableName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AppendReadOnlyRowsAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns,
            IReadOnlyList<IReadOnlyList<string?>> rows, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> ImportRowsAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns,
            IReadOnlyList<IReadOnlyList<string?>> rows, bool createTable, CancellationToken ct = default) => throw new NotSupportedException();
        public Task InsertRowAsync(Guid projectId, string tableName, IReadOnlyDictionary<string, string?> values, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> UpdateRowsAsync(Guid projectId, string tableName, IReadOnlyDictionary<string, string?> keys,
            IReadOnlyDictionary<string, string?> values, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> DeleteRowsAsync(Guid projectId, string tableName, IReadOnlyDictionary<string, string?> keys, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
