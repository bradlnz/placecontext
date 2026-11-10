using PlaceContext.Application;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.ProjectData;
using PlaceContext.Host.Components.ViewModels;
using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Tests;

public sealed class WorkflowSqlStudioTests
{
    private static readonly ProjectTableSchema Listings = new("listings",
    [
        new("id", "integer", true, true),
        new("title", "text", false, false),
        new("price", "numeric(10,2)", false, false),
        new("active", "boolean", false, false),
        new("created_at", "timestamp with time zone", false, false),
    ]);

    [Fact]
    public void Build_select_uses_canonical_project_columns_and_bounds_rows()
    {
        var sql = WorkflowSql.BuildSelect(
            Listings,
            ["title", "ID"],
            [new QueryDataFilter("title", "text", "O'Brien")],
            limit: 25);

        Assert.Contains("SELECT\n    \"title\",\n    \"id\"", sql);
        Assert.Contains("FROM\n    \"listings\"", sql);
        Assert.Contains("\"title\" = 'O''Brien'", sql);
        Assert.EndsWith("LIMIT 25", sql);
    }

    [Fact]
    public void Build_select_rejects_a_column_that_is_not_in_the_project_table()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            WorkflowSql.BuildSelect(Listings, ["title", "other_project.secret"], []));

        Assert.Contains("not a column of the project table 'listings'", error.Message);
    }

    [Fact]
    public void Filters_type_check_the_schema_column_and_escape_literals()
    {
        Assert.Equal(
            "\"price\" = 12.50",
            WorkflowSql.Predicate(Listings, new QueryDataFilter("price", "text", "12.50")));
        Assert.Equal(
            "\"active\" = TRUE",
            WorkflowSql.Predicate(Listings, new QueryDataFilter("active", "text", "true")));
        Assert.Equal(
            "\"title\" = 'x''; DROP TABLE listings; --'",
            WorkflowSql.Predicate(Listings, new QueryDataFilter("title", "text", "x'; DROP TABLE listings; --")));
        Assert.Throws<ArgumentException>(() =>
            WorkflowSql.Predicate(Listings, new QueryDataFilter("missing", "text", "x")));
    }

    [Fact]
    public void Unknown_table_references_are_reported_separately_from_the_project_schema()
    {
        var unknown = WorkflowSql.UnknownReferencedTables(
            "SELECT * FROM \"secret_table\" JOIN listings ON true",
            [Listings]);

        Assert.Equal(["secret_table"], unknown);
        Assert.Single(WorkflowSql.ReferencedTables("SELECT * FROM \"secret_table\" JOIN listings ON true", [Listings]));
    }

    [Fact]
    public void Ensure_limit_does_not_duplicate_an_existing_limit()
    {
        Assert.False(WorkflowSql.EnsureLimit("SELECT * FROM listings LIMIT 5", 100, out _));

        Assert.True(WorkflowSql.EnsureLimit("SELECT * FROM listings;", 100, out var bounded));
        Assert.Equal("SELECT * FROM listings\nLIMIT 100", bounded);
    }

    [Fact]
    public async Task Studio_loads_only_the_current_projects_schema_and_previews_through_the_project_query()
    {
        var projectId = Guid.NewGuid();
        var dispatcher = new Dispatcher();
        var vm = new JobChainsViewModel(
            new PlaceContextService(dispatcher, null!, null!, null!), null!, null!, null!);
        vm.Initialize(projectId);
        vm.NewChain();

        vm.OpenQueryDataAction(0);
        await vm.LoadQueryStudioAsync();

        var schemaQuery = Assert.Single(dispatcher.Queries.OfType<ListProjectTableSchemasQuery>());
        Assert.Equal(projectId, schemaQuery.ProjectId);
        Assert.Equal("listings", vm.QueryDataTableName);

        var pageQuery = Assert.Single(dispatcher.Queries.OfType<QueryProjectTablePageQuery>());
        Assert.Equal(projectId, pageQuery.ProjectId);
        Assert.Equal("listings", pageQuery.TableName);
        Assert.Contains("FROM\n    \"listings\"", vm.QueryDataSql);

        vm.AddQueryFilter("title", "O'Brien");
        var sql = Assert.IsType<string>(vm.BuildSelectedQuery());
        Assert.Contains("\"title\" = 'O''Brien'", sql);

        await vm.PreviewQueryDataAsync(sql);

        var previewQuery = Assert.Single(dispatcher.Queries.OfType<QueryProjectDataQuery>());
        Assert.Equal(projectId, previewQuery.ProjectId);
        Assert.Equal(sql, previewQuery.Sql);
        Assert.Same(dispatcher.PreviewResult, vm.QueryDataPreview);
        Assert.Null(vm.QueryDataPreviewError);
    }

    [Fact]
    public async Task Studio_rejects_a_filter_for_a_column_outside_the_loaded_table()
    {
        var dispatcher = new Dispatcher();
        var vm = new JobChainsViewModel(
            new PlaceContextService(dispatcher, null!, null!, null!), null!, null!, null!);
        vm.Initialize(Guid.NewGuid());
        vm.NewChain();
        vm.OpenQueryDataAction(0);
        await vm.LoadQueryStudioAsync();

        vm.AddQueryFilter("other_project.secret", "x");

        Assert.Empty(vm.QueryDataFilters);
        Assert.Contains("not a column of the project table 'listings'", vm.QueryDataHelperError);
    }

    private sealed class Dispatcher : IDispatcher
    {
        public List<object> Queries { get; } = [];
        public ProjectQueryResult PreviewResult { get; } = new(
            new[] { "id", "title" },
            new List<IReadOnlyList<string?>> { new string?[] { "1", "A" } },
            0,
            false);

        public Task<T> Send<T>(ICommand<T> command, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<T> Query<T>(IQuery<T> query, CancellationToken ct = default)
        {
            Queries.Add(query);
            var result = query switch
            {
                ListProjectTableSchemasQuery => (object)new[] { Listings },
                QueryProjectTablePageQuery page => new ProjectTablePageResult(
                    new[] { "id", "title" },
                    new List<IReadOnlyList<string?>> { new string?[] { "1", "A" } },
                    1,
                    page.Page,
                    page.PageSize,
                    true),
                QueryProjectDataQuery => PreviewResult,
                _ => throw new NotSupportedException(query.GetType().Name),
            };
            return Task.FromResult((T)result);
        }
    }
}
