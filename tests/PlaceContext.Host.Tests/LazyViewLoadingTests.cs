using Microsoft.AspNetCore.Components;
using PlaceContext.Application;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels;
using PlaceContext.Infrastructure.Operations;
using PlaceContext.Infrastructure.Observability;
using PlaceContext.TestSupport;

namespace PlaceContext.Host.Tests;

public sealed class LazyViewLoadingTests
{
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
                ListDataEntitiesQuery q => new DataEntityView[] { new(Guid.NewGuid(), q.ProjectId, "Records", "records", "name", [], [], DateTimeOffset.UtcNow) },
                QueryProjectTablePageQuery q => Page(q.Page),
                ListProjectDataTablesQuery => Array.Empty<ProjectTableInfo>(),
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
