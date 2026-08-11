using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

public class SearchTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);

    private static async Task<SearchHandler> SeedAsync(
        IOpenSearchDataGateway? openSearch = null,
        IPermissionService? permissions = null)
    {
        var projects = new InMemoryProjectRepository();
        var project = Project.Discover(ProjectPath.From("/home/brad/code/payments"), ProjectName.From("payments"), T0);
        project.Register(T0);
        await projects.AddAsync(project);

        var ledgers = new InMemoryActivityLogRepository();
        var ledger = ActivityLog.Start(project.Id);
        ledger.Append("Add payment webhook", Author.Agent("claude"), Rationale.None, TestDelta.None,
            ActivityVerification.None, new[] { "a.cs" }, Array.Empty<GraphNodeId>(), T0);
        await ledgers.SaveAsync(ledger);

        var decisions = new InMemoryDecisionRepository();
        await decisions.AddAsync(Decision.Record(project.Id, "Which payment provider?", "Stripe", Rationale.None, T0));

        return new SearchHandler(
            projects, ledgers, decisions,
            openSearch: openSearch,
            permissions: permissions);
    }

    [Theory]
    [InlineData("payments", "project")]
    [InlineData("webhook", "change")]
    [InlineData("provider", "decision")]
    public async Task Search_finds_hits_of_each_kind(string term, string kind)
    {
        var results = await (await SeedAsync()).HandleAsync(new SearchQuery(term));
        Assert.Contains(results.Hits, h => h.Kind == kind);
        Assert.All(results.Hits, h => Assert.StartsWith("/project/", h.Url));
    }

    [Fact]
    public async Task Context_documents_are_not_searched()
    {
        // Context markdown is agent-facing prose — search answers with data nodes, decisions,
        // changes, and artifacts instead.
        var results = await (await SeedAsync()).HandleAsync(new SearchQuery("Stripe"));
        var kinds = results.Hits.Select(h => h.Kind).ToHashSet();
        Assert.DoesNotContain("context", kinds);
        Assert.Contains("decision", kinds);
    }

    [Fact]
    public async Task Short_term_returns_nothing()
        => Assert.Empty((await (await SeedAsync()).HandleAsync(new SearchQuery("a"))).Hits);

    [Fact]
    public async Task OpenSearch_maps_address_and_name_results()
    {
        var projectId = Guid.NewGuid();
        var gateway = new FakeOpenSearchGateway(_ => new OpenSearchSearchView(
            2,
            3,
            new[]
            {
                Hit("properties", "address-1", ("full_address", "12 Smith Street, Brisbane"),
                    ("suburb", "Brisbane")),
                Hit("places", "place-1", ("official_name", "Brisbane City Hall"),
                    ("type", "Landmark")),
            },
            null));

        var results = await (await SeedAsync(gateway, new FakePermissionService(true)))
            .HandleAsync(new SearchQuery("Brisbane", ProjectId: projectId));

        Assert.Collection(results.Hits.Where(hit => hit.Kind == "opensearch"),
            address =>
            {
                Assert.Equal("12 Smith Street, Brisbane", address.Title);
                Assert.Equal("properties · Brisbane", address.Subtitle);
            },
            place =>
            {
                Assert.Equal("Brisbane City Hall", place.Title);
                Assert.Equal("places · Landmark", place.Subtitle);
            });
    }

    [Fact]
    public async Task OpenSearch_runs_one_bounded_free_text_query_for_active_project()
    {
        var projectId = Guid.NewGuid();
        var gateway = new FakeOpenSearchGateway(_ => EmptyOpenSearch());

        await (await SeedAsync(gateway, new FakePermissionService(true)))
            .HandleAsync(new SearchQuery("  Smith Street  ", ProjectId: projectId));

        var request = Assert.Single(gateway.Requests);
        Assert.Equal(projectId, request.ProjectId);
        Assert.Equal("*", request.IndexPattern);
        Assert.Equal("Smith Street", request.QueryText);
        Assert.Equal(1, request.Page);
        Assert.Equal(8, request.PageSize);
        Assert.Null(request.BucketField);
    }

    [Fact]
    public async Task OpenSearch_is_not_queried_without_data_read()
    {
        var gateway = new FakeOpenSearchGateway(_ => EmptyOpenSearch());

        await (await SeedAsync(gateway, new FakePermissionService(false)))
            .HandleAsync(new SearchQuery("payments", ProjectId: Guid.NewGuid()));

        Assert.Empty(gateway.Requests);
    }

    [Fact]
    public async Task OpenSearch_failure_does_not_break_workspace_search()
    {
        var gateway = new FakeOpenSearchGateway(_ => throw new InvalidOperationException("unavailable"));

        var results = await (await SeedAsync(gateway, new FakePermissionService(true)))
            .HandleAsync(new SearchQuery("payments", ProjectId: Guid.NewGuid()));

        Assert.Contains(results.Hits, hit => hit.Kind == "project" && hit.Title == "payments");
    }

    [Fact]
    public async Task OpenSearch_deep_link_encodes_index_query_and_document_id()
    {
        var projectId = Guid.NewGuid();
        var gateway = new FakeOpenSearchGateway(_ => new OpenSearchSearchView(
            1, 1,
            new[] { Hit("property records", "doc/1+2", ("address", "12 Smith Street")) },
            null));

        var results = await (await SeedAsync(gateway, new FakePermissionService(true)))
            .HandleAsync(new SearchQuery("Smith & Sons", ProjectId: projectId));

        var hit = Assert.Single(results.Hits, item => item.Kind == "opensearch");
        Assert.Equal($"/project/{projectId}/data-search?index=property%20records&q=Smith%20%26%20Sons&document=doc%2F1%2B2", hit.Url);
    }

    [Fact]
    public async Task OpenSearch_document_with_artifact_id_opens_the_artifact()
    {
        var projectId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();
        var gateway = new FakeOpenSearchGateway(_ => new OpenSearchSearchView(
            1, 1,
            new[] { Hit("reports", "doc-1", ("title", "Feasibility report"),
                ("artifact_id", artifactId.ToString())) },
            null));

        var results = await (await SeedAsync(gateway, new FakePermissionService(true)))
            .HandleAsync(new SearchQuery("Feasibility", ProjectId: projectId));

        var hit = Assert.Single(results.Hits, item => item.Kind == "opensearch");
        Assert.Equal($"/artifacts?artifact={artifactId}", hit.Url);
    }

    private static OpenSearchHitView Hit(
        string index, string id, params (string Key, string Value)[] fields)
        => new(index, id, 1, fields.ToDictionary(field => field.Key, field => (string?)field.Value));

    private static OpenSearchSearchView EmptyOpenSearch()
        => new(0, 1, Array.Empty<OpenSearchHitView>(), null);

    private sealed class FakeOpenSearchGateway : IOpenSearchDataGateway
    {
        private readonly Func<OpenSearchSearchRequest, OpenSearchSearchView> _search;

        public FakeOpenSearchGateway(Func<OpenSearchSearchRequest, OpenSearchSearchView> search)
            => _search = search;

        public List<OpenSearchSearchRequest> Requests { get; } = new();

        public Task<IReadOnlyList<OpenSearchIndexView>> ListIndicesAsync(
            Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OpenSearchIndexView>>(Array.Empty<OpenSearchIndexView>());

        public Task<IReadOnlyList<OpenSearchFieldView>> ListFieldsAsync(
            Guid projectId, string indexPattern, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OpenSearchFieldView>>(Array.Empty<OpenSearchFieldView>());

        public Task<OpenSearchLastUpdatedView> GetLastUpdatedAsync(
            Guid projectId, string indexPattern, IReadOnlyList<string> candidateFields,
            CancellationToken ct = default)
            => Task.FromResult(new OpenSearchLastUpdatedView(null, null));

        public Task<OpenSearchSearchView> SearchAsync(
            OpenSearchSearchRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(_search(request));
        }

        public Task CreateIndexAsync(
            Guid projectId, string indexName, IReadOnlyList<OpenSearchMappingField> mappingFields,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<int> IndexBulkAsync(
            Guid projectId, string indexName, IReadOnlyList<string> columnNames,
            IReadOnlyList<IReadOnlyList<string?>> rows, CancellationToken ct = default,
            IReadOnlyList<string>? jsonColumnNames = null)
            => Task.FromResult(rows.Count);

        public Task DeleteIndexAsync(Guid projectId, string indexName, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<ProjectQueryResult> SearchSqlAsync(
            Guid projectId, string sql, CancellationToken ct = default)
            => Task.FromResult(new ProjectQueryResult(
                new[] { "index" }, new IReadOnlyList<string?>[] { new[] { sql } }, 0, false));

        public Task<OpenSearchExportView> ExportIndexAsync(
            Guid projectId, string indexPattern, int maxRows = 500,
            CancellationToken ct = default)
            => Task.FromResult(new OpenSearchExportView(
                Array.Empty<OpenSearchExportField>(), Array.Empty<IReadOnlyList<string?>>(), false));
    }

    private sealed class FakePermissionService : IPermissionService
    {
        private readonly bool _hasDataRead;
        public FakePermissionService(bool hasDataRead) => _hasDataRead = hasDataRead;

        public Task<bool> HasAsync(string permission, CancellationToken ct = default)
            => Task.FromResult(permission == Permission.DataRead && _hasDataRead);

        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlySet<string>>(_hasDataRead
                ? new HashSet<string> { Permission.DataRead }
                : new HashSet<string>());

        public Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(
            Guid userId, string roleName, CancellationToken ct = default)
            => GetEffectivePermissionsAsync(ct);
    }
}
