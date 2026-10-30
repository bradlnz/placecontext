using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels;
using PlaceContext.Infrastructure.Operations;
using PlaceContext.Infrastructure.Observability;
using PlaceContext.Infrastructure.Tenancy;
using PlaceContext.TestSupport;

namespace PlaceContext.Host.Tests;

public sealed class LazyViewLoadingTests
{
    [Fact]
    public async Task Observability_coalesces_operation_bursts_and_reloads_changes_arriving_during_a_query()
    {
        var dispatcher = new Dispatcher();
        var operations = new OperationCenter(null!, null!);
        var tenant = new FakeCurrentTenant(Guid.NewGuid());
        using var vm = new ObservabilityViewModel(Service(dispatcher), new PortalUiState(), new Navigation(),
            operations, null!, null!, null!, tenant: tenant);
        await vm.InitializeAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        dispatcher.Delay = query =>
        {
            if (query is not ListRecentRunReportsQuery) return null;
            if (Interlocked.Increment(ref calls) == 1)
            {
                started.SetResult();
                return response.Task;
            }
            refreshed.TrySetResult();
            return null;
        };
        var info = new TenantInfo(tenant.TenantId, "own", "Own", "UTC");
        for (var i = 0; i < 100; i++) operations.Track(info, null, "burst", null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, calls);
        operations.Track(info, null, "arrived during query", null);
        response.SetResult(Array.Empty<RunReportView>());
        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Workspace_search_waits_for_typing_to_pause_and_ignores_stale_responses()
    {
        var dispatcher = new Dispatcher();
        using var services = new ServiceCollection().AddSingleton(Service(dispatcher)).BuildServiceProvider();
        using var vm = new MainLayoutViewModel(new PortalUiState(), new ConfigurationBuilder().Build(),
            new FakeCurrentTenant(Guid.NewGuid()), new OperationCenter(null!, null!), null!, new Navigation(),
            services.GetRequiredService<IServiceScopeFactory>());
        await Task.WhenAll(vm.SearchAsync("fi"), vm.SearchAsync("fir"), vm.SearchAsync("first"));
        Assert.Equal("first", Assert.Single(dispatcher.Queries.OfType<SearchQuery>()).Term);

        var started = new TaskCompletionSource();
        var stale = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.Delay = query =>
        {
            if (query is not SearchQuery { Term: "old" }) return null;
            started.SetResult();
            return stale.Task;
        };
        var oldSearch = vm.SearchAsync("old");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await vm.SearchAsync("new");
        stale.SetResult(new SearchResultsView("old", []));
        await oldSearch;
        Assert.Equal("new", vm.SearchResults?.Term);
        Assert.False(vm.Searching);

        var count = dispatcher.Queries.Count;
        var cancelled = vm.SearchAsync("cancelled");
        vm.CloseSearch();
        await cancelled;
        Assert.Equal(count, dispatcher.Queries.Count);
        await vm.SearchAsync("x");
        Assert.Null(vm.SearchResults);
    }

    [Fact]
    public async Task Dashboard_charts_do_not_wait_for_history_or_workflow_definitions()
    {
        var dispatcher = new Dispatcher();
        var history = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var chains = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.Delay = query => query switch
        {
            ListRecentRunReportsQuery => history.Task,
            ListJobChainsQuery => chains.Task,
            _ => null
        };
        var ui = new PortalUiState();
        ui.SetProject(Guid.NewGuid(), "Project");
        using var vm = new DashboardViewModel(Service(dispatcher), ui, new Navigation(), null!,
            new FakeCurrentTenant(Guid.NewGuid()), new OperationCenter(null!, null!));
        var loading = vm.LoadAsync();
        Assert.False(vm.Ready);
        Assert.False(vm.ChainsReady);
        Assert.Single(vm.SqlCharts());
        Assert.Single(vm.Entities);
        history.SetResult(Array.Empty<RunReportView>());
        chains.SetResult(Array.Empty<JobChainView>());
        await loading;
        Assert.True(vm.Ready);
        Assert.True(vm.ChainsReady);
        Assert.DoesNotContain(dispatcher.Queries, query => query is ListJobsQuery);
        var chain = new JobChainView(Guid.NewGuid(), ui.CurrentProjectId!.Value, "Workflow", null, [], DateTimeOffset.UtcNow);
        await vm.PrepareQuickChainRunAsync(chain);
        Assert.Single(dispatcher.Queries.OfType<ListJobsQuery>());
        Assert.Equal(chain, vm.RunPromptChain);
    }

    [Fact]
    public async Task Dashboard_operation_changes_only_refresh_history_for_its_own_tenant()
    {
        var dispatcher = new Dispatcher();
        var tenant = new FakeCurrentTenant(Guid.NewGuid());
        var operations = new OperationCenter(null!, null!);
        var ui = new PortalUiState();
        ui.SetProject(Guid.NewGuid(), "Project");
        using var vm = new DashboardViewModel(Service(dispatcher), ui, new Navigation(), null!, tenant, operations);
        vm.Initialize();
        await vm.LoadAsync();
        var count = dispatcher.Queries.Count;
        operations.Track(new TenantInfo(Guid.NewGuid(), "other", "Other", "UTC"), null, "Other tenant", null);
        Assert.Equal(count, dispatcher.Queries.Count);
        operations.Track(new TenantInfo(tenant.TenantId, "own", "Own", "UTC"), null, "Own tenant", null);
        Assert.IsType<ListRecentRunReportsQuery>(Assert.Single(dispatcher.Queries.Skip(count)));
    }

    [Theory]
    [InlineData("runs", typeof(ListRecentRunReportsQuery))]
    [InlineData("workflows", typeof(ListRecentChainRunsQuery))]
    [InlineData("chains", typeof(ListRecentChainRunsQuery))]
    [InlineData("logs", typeof(ListRecentRunReportsQuery))]
    [InlineData("traces", null)]
    public async Task Observability_loads_only_the_initial_tabs_data(string tab, Type? queryType)
    {
        var dispatcher = new Dispatcher();
        using var telemetry = new JobTelemetryCollector();
        using var vm = new ObservabilityViewModel(Service(dispatcher), new PortalUiState(),
            new Navigation($"http://localhost/observability?tab={tab}"),
            new OperationCenter(null!, null!), telemetry, null!, null!);
        await vm.InitializeAsync();
        if (queryType is null) Assert.Empty(dispatcher.Queries);
        else Assert.IsType(queryType, Assert.Single(dispatcher.Queries));
        if (tab == "logs") Assert.NotNull(vm.LogResults);
        var count = dispatcher.Queries.Count;
        await vm.RestoreTabAsync(tab);
        Assert.Equal(count, dispatcher.Queries.Count);
    }

    [Fact]
    public async Task Opening_a_live_trace_loads_its_run_without_fetching_history()
    {
        var dispatcher = new Dispatcher();
        var run = new JobRunDetailView(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Succeeded",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [], null,
            new("code", "rust", null, null, null, 1, 1, false));
        dispatcher.Delay = query => query switch
        {
            GetJobRunQuery => Task.FromResult<object>(run),
            GetJobQuery or GetProjectByIdQuery => Task.FromResult<object>(null!),
            _ => null,
        };
        using var telemetry = new JobTelemetryCollector();
        using var vm = new ObservabilityViewModel(Service(dispatcher), new PortalUiState(),
            new Navigation("http://localhost/observability?tab=traces"),
            new OperationCenter(null!, null!), telemetry, null!, null!);
        await vm.InitializeAsync();
        await vm.OpenLiveTrace(new(run.Id, run.JobId, "Job", run.ProjectId, run.Status, false,
            run.StartedAt, 100, []));
        Assert.Equal(run.Id, vm.Open?.Run.Id);
        Assert.DoesNotContain(dispatcher.Queries, query => query is ListRecentRunReportsQuery or ListRecentChainRunsQuery);
    }

    [Fact]
    public async Task Observability_shell_is_ready_before_history_and_other_tabs_load_on_demand()
    {
        var dispatcher = new Dispatcher();
        var history = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.Delay = query => query is ListRecentRunReportsQuery ? history.Task : null;
        using var vm = new ObservabilityViewModel(Service(dispatcher), new PortalUiState(), new Navigation(),
            new OperationCenter(null!, null!), null!, null!, null!);
        var loading = vm.InitializeAsync();
        Assert.False(vm.Loading);
        Assert.False(loading.IsCompleted);
        Assert.Null(vm.ChainReports);
        history.SetResult(Array.Empty<RunReportView>());
        await loading;
        dispatcher.Delay = null;
        await vm.RestoreTabAsync("workflows");
        Assert.NotNull(vm.ChainReports);
        Assert.Equal(2, dispatcher.Queries.Count);
        await vm.RefreshAsync();
        Assert.Equal(2, dispatcher.Queries.OfType<ListRecentChainRunsQuery>().Count());
        Assert.Single(dispatcher.Queries.OfType<ListRecentRunReportsQuery>());
        await vm.RestoreTabAsync("runs");
        Assert.Equal(3, dispatcher.Queries.Count);
    }

    [Fact]
    public async Task Dashboard_loads_charts_when_the_shell_selects_its_project_after_initial_load()
    {
        var dispatcher = new Dispatcher();
        var ui = new PortalUiState();
        using var vm = new DashboardViewModel(Service(dispatcher), ui, new Navigation(), null!,
            new FakeCurrentTenant(Guid.NewGuid()), new OperationCenter(null!, null!));
        vm.Initialize();
        await vm.LoadAsync();
        Assert.Empty(vm.SqlCharts());

        var projectId = Guid.NewGuid();
        ui.SetProject(projectId, "Project");

        Assert.Equal(projectId, Assert.Single(dispatcher.Queries.OfType<ListProjectChartsQuery>()).ProjectId);
        Assert.Single(vm.SqlCharts());
        ui.Set("Dashboard", "Updated subtitle");
        Assert.Single(dispatcher.Queries.OfType<ListProjectChartsQuery>());
        vm.Dispose();
        ui.SetProject(Guid.NewGuid(), "Other project");
        Assert.Single(dispatcher.Queries.OfType<ListProjectChartsQuery>());
    }

    [Fact]
    public async Task Dashboard_discards_pending_charts_when_the_selected_project_changes()
    {
        var dispatcher = new Dispatcher();
        var ui = new PortalUiState();
        var oldProject = Guid.NewGuid();
        ui.SetProject(oldProject, "Old project");
        var pending = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.Delay = query => query is ListProjectChartsQuery q && q.ProjectId == oldProject
            ? pending.Task : null;
        using var vm = new DashboardViewModel(Service(dispatcher), ui, new Navigation(), null!,
            new FakeCurrentTenant(Guid.NewGuid()), new OperationCenter(null!, null!));
        vm.Initialize();
        var loading = vm.LoadAsync();
        Assert.False(loading.IsCompleted);

        var projectId = Guid.NewGuid();
        ui.SetProject(projectId, "New project");
        Assert.Equal("sql:Summary", Assert.Single(vm.SqlCharts()).TableName);
        var queryCount = dispatcher.Queries.Count;
        pending.SetResult(new ProjectChartView[] { new("sql:Old", "{}", DateTimeOffset.UtcNow) });
        await loading;

        Assert.Equal("sql:Summary", Assert.Single(vm.SqlCharts()).TableName);
        Assert.Equal(projectId, Assert.Single(vm.Entities).ProjectId);
        Assert.Equal(queryCount, dispatcher.Queries.Count);
    }

    [Fact]
    public async Task Dashboard_defers_entity_scans_until_requested_and_avoids_duplicate_loads()
    {
        var dispatcher = new Dispatcher();
        var ui = new PortalUiState();
        ui.SetProject(Guid.NewGuid(), "Project");
        using var vm = new DashboardViewModel(Service(dispatcher), ui, new Navigation(), null!,
            new FakeCurrentTenant(Guid.NewGuid()), new OperationCenter(null!, null!));
        await vm.LoadAsync();
        Assert.True(vm.Ready);
        Assert.Single(vm.Entities);
        Assert.Empty(dispatcher.Commands);
        Assert.DoesNotContain(dispatcher.Queries, query => query is ListProjectTableColumnsQuery);

        var columns = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.Delay = query => query is ListProjectTableColumnsQuery ? columns.Task : null;
        var loading = vm.LoadEntityChartsAsync();
        Assert.True(vm.EntityChartsLoading);
        await vm.LoadEntityChartsAsync();
        Assert.Single(dispatcher.Queries.OfType<ListProjectTableColumnsQuery>());
        columns.SetResult(new ProjectColumnInfo[] { new("category", "text", true, false) });
        await loading;
        Assert.False(vm.EntityChartsLoading);
        Assert.IsType<ExecuteProjectDataCommand>(Assert.Single(dispatcher.Commands));
    }

    [Fact]
    public async Task Dashboard_summary_failure_can_be_retried_and_project_changes_stop_pending_scans()
    {
        var dispatcher = new Dispatcher();
        var ui = new PortalUiState();
        ui.SetProject(Guid.NewGuid(), "Project");
        using var vm = new DashboardViewModel(Service(dispatcher), ui, new Navigation(), null!,
            new FakeCurrentTenant(Guid.NewGuid()), new OperationCenter(null!, null!));
        await vm.LoadAsync();
        dispatcher.Delay = query => query is ListProjectTableColumnsQuery
            ? Task.FromException<object>(new InvalidOperationException("offline")) : null;
        await vm.LoadEntityChartsAsync();
        Assert.Equal("offline", vm.EntityChartsError);
        Assert.False(vm.EntityChartsLoading);

        var columns = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.Delay = query => query is ListProjectTableColumnsQuery ? columns.Task : null;
        var loading = vm.LoadEntityChartsAsync();
        ui.SetProject(Guid.NewGuid(), "Other project");
        columns.SetResult(new ProjectColumnInfo[] { new("category", "text", true, false) });
        await loading;
        Assert.Null(vm.EntityChartsError);
        Assert.False(vm.EntityChartsLoading);
        Assert.Empty(dispatcher.Commands);
        Assert.Equal(2, dispatcher.Queries.OfType<ListProjectTableColumnsQuery>().Count());
    }

    [Fact]
    public async Task Entity_shell_is_ready_while_the_first_record_page_is_pending()
    {
        var dispatcher = new Dispatcher();
        var records = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.Delay = query => query is QueryProjectTablePageQuery ? records.Task : null;
        var vm = new EntityBrowseViewModel(Service(dispatcher), null!) { EntityName = "Records" };
        vm.Initialize(Guid.NewGuid());

        var loading = vm.LoadAsync();
        Assert.True(vm.Loaded);
        Assert.Null(vm.Page);
        Assert.False(Assert.Single(dispatcher.Queries.OfType<QueryProjectTablePageQuery>()).IncludeTotalCount);
        Assert.Equal("Page 1", vm.PageLabel(new ProjectTablePageResult([], [], 51, 1, 50, false)));
        Assert.False(loading.IsCompleted);
        Assert.Empty(dispatcher.Commands);
        Assert.DoesNotContain(dispatcher.Queries, query => query is ListProjectChartsQuery);

        records.SetResult(Page(1));
        await loading;
        Assert.Equal("row 1", vm.PageRows[0][0]);
        dispatcher.Delay = null;
        await vm.GoToPageAsync(2);
        Assert.Equal("row 2", vm.PageRows[0][0]);
        Assert.All(dispatcher.Queries.OfType<QueryProjectTablePageQuery>(), query => Assert.Equal(50, query.PageSize));

        await vm.SelectGraph();
        Assert.IsType<ExecuteProjectDataCommand>(Assert.Single(dispatcher.Commands));
    }

    [Fact]
    public async Task Switching_entities_discards_a_pending_record_page()
    {
        var dispatcher = new Dispatcher();
        var oldProject = Guid.NewGuid();
        var old = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.Delay = query => query is QueryProjectTablePageQuery q && q.ProjectId == oldProject ? old.Task : null;
        var vm = new EntityBrowseViewModel(Service(dispatcher), null!) { EntityName = "Records" };
        vm.Initialize(oldProject);
        var loading = vm.LoadAsync();
        vm.Initialize(Guid.NewGuid());
        await vm.LoadAsync();
        old.SetResult(Page(99));
        await loading;
        Assert.Equal("row 1", vm.PageRows[0][0]);
    }

    [Fact]
    public async Task Table_catalogue_and_closed_chart_editors_do_not_discover_schemas_or_indices()
    {
        var dispatcher = new Dispatcher();
        var vm = new ProjectDataViewModel(Service(dispatcher), new PortalUiState(), null!);
        vm.Initialize(Guid.NewGuid());
        await vm.LoadAsync();
        await vm.AfterRenderAsync();
        Assert.IsType<ListProjectDataTablesQuery>(Assert.Single(dispatcher.Queries));

        vm.ShowPane(SidebarPane.Indexes);
        Assert.Contains(dispatcher.Queries, query => query is ListOpenSearchIndicesQuery);

        dispatcher.Queries.Clear();
        var analytics = new ProjectAnalyticsViewModel(Service(dispatcher), new PortalUiState(), null!, null!);
        await analytics.RenderAsync();
        Assert.Empty(dispatcher.Queries);
    }

    [Fact]
    public async Task Sql_editor_mounts_while_batched_autocomplete_is_pending()
    {
        var dispatcher = new Dispatcher();
        var pending = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.Delay = query => query is ListProjectTableSchemasQuery ? pending.Task : null;
        var js = new EditorJs();
        var vm = new ProjectDataViewModel(Service(dispatcher), new PortalUiState(), js);
        vm.Initialize(Guid.NewGuid());
        await vm.ShowSqlTabAsync("records", "SELECT name FROM records LIMIT 50");
        await vm.AfterRenderAsync();
        Assert.Contains("pcmonaco.init", js.Calls);
        Assert.Single(dispatcher.Queries.OfType<ListProjectTableSchemasQuery>());
        Assert.DoesNotContain(dispatcher.Queries, query => query is ListProjectTableColumnsQuery);
        pending.SetResult(Array.Empty<ProjectTableSchema>());
        await js.SchemaPushed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.DoesNotContain(dispatcher.Queries, query => query is ListOpenSearchIndicesQuery);
    }

    [Fact]
    public async Task Table_preview_projects_named_columns_with_a_small_row_limit()
    {
        var dispatcher = new Dispatcher();
        dispatcher.Delay = query => query is ListProjectTableColumnsQuery
            ? Task.FromResult<object>(new ProjectColumnInfo[] { new("Order", "text", false, false), new("a\"b", "text", false, false) }) : null;
        var vm = new ProjectDataViewModel(Service(dispatcher), new PortalUiState(), null!);
        vm.Initialize(Guid.NewGuid());
        Assert.Equal("SELECT \"Order\", \"a\"\"b\" FROM \"records\" LIMIT 50;", await vm.TablePreviewSqlAsync("records"));
    }

    private sealed class EditorJs : Microsoft.JSInterop.IJSRuntime
    {
        public List<string> Calls { get; } = [];
        public TaskCompletionSource SchemaPushed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args)
        {
            Calls.Add(identifier);
            if (identifier == "pcmonaco.setSqlSchema") SchemaPushed.TrySetResult();
            return ValueTask.FromResult(typeof(T) == typeof(bool) ? (T)(object)true : default!);
        }
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsync<T>(identifier, args);
    }

    [Fact]
    public async Task Project_details_load_only_for_the_selected_tab_and_are_reused()
    {
        var dispatcher = new Dispatcher();
        var vm = new ProjectViewModel(Service(dispatcher), new PortalUiState(), new Navigation(), null!);
        await vm.LoadAsync(Guid.NewGuid());
        Assert.IsType<GetProjectOverviewQuery>(Assert.Single(dispatcher.Queries));

        await vm.SwitchTab(ProjectViewModel.Tabs.Requirements);
        Assert.NotNull(vm.Requirements);
        Assert.Null(vm.Timeline);
        Assert.Null(vm.Decisions);
        await vm.SwitchTab(ProjectViewModel.Tabs.Activity);
        Assert.NotNull(vm.Timeline);
        Assert.NotNull(vm.Decisions);
        var count = dispatcher.Queries.Count;
        await vm.SwitchTab(ProjectViewModel.Tabs.Requirements);
        Assert.Equal(count, dispatcher.Queries.Count);
    }

    [Fact]
    public async Task Jobs_catalogue_defers_editor_metadata_until_a_pane_opens()
    {
        var dispatcher = new Dispatcher();
        var vm = new JobsViewModel(Service(dispatcher), new OperationCenter(null!, null!), null!, new Navigation());
        try
        {
            await vm.LoadPageAsync(Guid.NewGuid(), null, null);
            await vm.AfterRenderAsync();
            Assert.Equal(2, dispatcher.Queries.Count);
            Assert.Null(vm.VaultSecrets);
            vm.NewJob();
            await vm.AfterRenderAsync();
            Assert.NotNull(vm.VaultSecrets);
            Assert.NotNull(vm.ProjectMcpConnections);
            var count = dispatcher.Queries.Count;
            await vm.AfterRenderAsync();
            Assert.Equal(count, dispatcher.Queries.Count);
        }
        finally { vm.DetachEvents(); }
    }

    [Fact]
    public async Task OpenSearch_waits_for_the_chart_action_before_running_aggregations()
    {
        var dispatcher = new Dispatcher();
        var vm = new OpenSearchDataViewModel(Service(dispatcher), new PortalUiState(), null!, new Permissions()) { ProjectId = Guid.NewGuid() };
        await vm.SetParametersAsync();
        Assert.True(vm.InitialLoadComplete);
        Assert.False(vm.ChartsRequested);
        Assert.DoesNotContain(dispatcher.Queries, query => query is SearchOpenSearchQuery);
        await vm.SelectIndexAsync(new("records", 1000000, null));
        Assert.DoesNotContain(dispatcher.Queries, query => query is SearchOpenSearchQuery);
        await vm.GenerateChartsAsync();
        Assert.True(vm.ChartsRequested);
        Assert.Contains(dispatcher.Queries, query => query is SearchOpenSearchQuery);
    }

    [Fact]
    public async Task Artifacts_fetch_bounded_batches_and_search_beyond_the_first_batch()
    {
        var dispatcher = new Dispatcher();
        var vm = Artifacts(dispatcher);
        await vm.LoadFilesAsync();
        Assert.Equal(100, vm.Files.Count);
        Assert.True(vm.LoadMayBeIncomplete);
        await vm.LoadMoreFilesAsync();
        await vm.LoadMoreFilesAsync();
        Assert.Equal(230, vm.Files.Count);
        Assert.False(vm.LoadMayBeIncomplete);
        Assert.Equal(new[] { 0, 100, 200 }, dispatcher.Queries.OfType<ListRecentArtifactsQuery>().Select(query => query.Skip));
        Assert.All(dispatcher.Queries.OfType<ListRecentArtifactsQuery>(), query => Assert.Equal(101, query.Take));

        vm.Filter = "file 229";
        await vm.LoadFilesAsync();
        Assert.Equal("file 229", Assert.Single(vm.Files).Title);
        Assert.False(vm.LoadMayBeIncomplete);
    }

    [Fact]
    public async Task Old_artifact_responses_cannot_replace_new_filters()
    {
        var dispatcher = new Dispatcher();
        var old = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.Delay = query => query is ListRecentArtifactsQuery { Search: null } ? old.Task : null;
        var vm = Artifacts(dispatcher);
        var loading = vm.LoadFilesAsync();
        Assert.True(vm.FilesLoading);
        vm.Filter = "file 229";
        await vm.LoadFilesAsync();
        old.SetResult(Array.Empty<ArtifactFileView>());
        await loading;
        Assert.Equal("file 229", Assert.Single(vm.Files).Title);
        Assert.False(vm.FilesLoading);
    }

    [Fact]
    public async Task Failed_artifact_batch_keeps_existing_files_and_retries_the_same_offset()
    {
        var dispatcher = new Dispatcher();
        var vm = Artifacts(dispatcher);
        vm.ProjectFilter = Guid.NewGuid().ToString();
        await vm.LoadFilesAsync();
        dispatcher.Delay = _ => Task.FromException<object>(new InvalidOperationException("offline"));
        await vm.LoadMoreFilesAsync();
        Assert.Equal(100, vm.Files.Count);
        Assert.Equal("offline", vm.FilesError);
        dispatcher.Delay = null;
        await vm.LoadMoreFilesAsync();
        Assert.Equal(200, vm.Files.Count);
        Assert.Null(vm.FilesError);
        Assert.Equal(new[] { 0, 100, 100 }, dispatcher.Queries.OfType<ListProjectArtifactsQuery>().Select(query => query.Skip));
    }

    private static PlaceContextService Service(Dispatcher dispatcher) => new(dispatcher, null!, null!, null!);
    private static ArtifactsViewModel Artifacts(Dispatcher dispatcher) => new(Service(dispatcher), new PortalUiState(), new Navigation(), null!, null!, null!, null!, null!);
    private static ProjectTablePageResult Page(int page) => new(["name"], [new string?[] { $"row {page}" }], 100, page, 50);

    private sealed class Dispatcher : IDispatcher
    {
        public List<object> Queries { get; } = [];
        public List<object> Commands { get; } = [];
        public Func<object, Task<object>?>? Delay { get; set; }
        private readonly ArtifactFileView[] _files = Enumerable.Range(0, 230).Select(index => new ArtifactFileView(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, "report", $"file {index}", "text/plain", 1, DateTimeOffset.UtcNow)).ToArray();

        public Task<T> Send<T>(ICommand<T> command, CancellationToken ct = default)
        {
            Commands.Add(command);
            return Task.FromResult((T)(object)new ProjectQueryResult(["name"], [], 0, false));
        }

        public async Task<T> Query<T>(IQuery<T> query, CancellationToken ct = default)
        {
            Queries.Add(query);
            if (Delay?.Invoke(query) is { } pending) return (T)await pending;
            object result = query switch
            {
                SearchQuery q => new SearchResultsView(q.Term, []),
                ListDataEntitiesQuery q => new DataEntityView[] { new(Guid.NewGuid(), q.ProjectId, "Records", "records", "name", [], [], DateTimeOffset.UtcNow) },
                QueryProjectTablePageQuery q => Page(q.Page),
                ListProjectDataTablesQuery => Array.Empty<ProjectTableInfo>(),
                ListProjectChartsQuery => new ProjectChartView[] { new("sql:Summary", "{\"type\":\"bar\"}", DateTimeOffset.UtcNow) },
                ListOpenSearchIndicesQuery => Array.Empty<OpenSearchIndexView>(),
                ListOpenSearchDashboardsQuery => Array.Empty<OpenSearchDashboardView>(),
                ListOpenSearchFieldsQuery => new OpenSearchFieldView[] { new("category", "keyword", true, true) },
                GetOpenSearchLastUpdatedQuery => new OpenSearchLastUpdatedView(null, null),
                SearchOpenSearchQuery => new OpenSearchSearchView(0, 0, [], null),
                GetProjectOverviewQuery q => new ProjectOverviewView(q.ProjectId, "Project", "/project", "Ready", null, null, 0, 0, [], 0),
                GetProjectRequirementsQuery q => RequirementsView.EmptyForProject(q.ProjectId),
                GetTimelineQuery q => new ActivityTimelineView(q.ProjectId, []),
                GetDecisionsQuery => Array.Empty<DecisionView>(),
                ListJobsQuery => Array.Empty<JobView>(),
                ListJobChainsQuery => Array.Empty<JobChainView>(),
                ListRecentRunReportsQuery => Array.Empty<RunReportView>(),
                ListRecentChainRunsQuery => Array.Empty<ChainRunReportView>(),
                ListTriggersQuery => Array.Empty<TriggerView>(),
                ListProjectSecretsQuery => Array.Empty<ProjectSecretView>(),
                ListEventTypesQuery => Array.Empty<EventTypeView>(),
                ListMcpConnectionsQuery => Array.Empty<McpConnectionView>(),
                ListRecentArtifactsQuery q => Files(q.Take, q.Skip, q.Search),
                ListProjectArtifactsQuery q => Files(q.Take, q.Skip, q.Search),
                _ => throw new NotSupportedException(query.GetType().Name)
            };
            return (T)result;
        }

        private ArtifactFileView[] Files(int take, int skip, string? search) => _files
            .Where(file => search is null || file.Title.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Skip(skip).Take(take).ToArray();
    }

    private sealed class Navigation : NavigationManager
    {
        public Navigation(string uri = "http://localhost/project") => Initialize("http://localhost/", uri);
    }

    private sealed class Permissions : IPermissionService
    {
        public Task<bool> HasAsync(string permission, CancellationToken ct = default) => Task.FromResult(false);
        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(Guid userId, string roleName, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
