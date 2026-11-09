using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Operations;
using PlaceContext.Application.ProjectData;
using PlaceContext.Application.Tenancy;
using PlaceContext.Application;
using PlaceContext.Host.Components.ViewModels;
using PlaceContext.Infrastructure.Observability;
using PlaceContext.TestSupport;

namespace PlaceContext.Host.Tests;

public sealed class LazyViewLoadingTests
{
    [Fact]
    public async Task Observability_coalesces_operation_bursts_and_reloads_changes_arriving_during_a_query()
    {
        var dispatcher = new Dispatcher();
        var operations = new OperationCenter(null!, default);
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
            if (query is not ListRecentRunReportsQuery)
            {
                return null;
            }
            if (Interlocked.Increment(ref calls) == 1)
            {
                started.SetResult();
                return response.Task;
            }
            refreshed.TrySetResult();
            return null;
        };
        var info = new TenantInfo(tenant.TenantId, "own", "Own", "UTC");
        for (var i = 0; i < 100; i++)
        {
            operations.Track(info, null, "burst", null);
        }
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
            new FakeCurrentTenant(Guid.NewGuid()), new OperationCenter(null!, default), null!, new Navigation(),
            services.GetRequiredService<IServiceScopeFactory>());
        await Task.WhenAll(vm.SearchAsync("fi"), vm.SearchAsync("fir"), vm.SearchAsync("first"));
        Assert.Equal("first", Assert.Single(dispatcher.Queries.OfType<SearchQuery>()).Term);

        var started = new TaskCompletionSource();
        var stale = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.Delay = query =>
        {
            if (query is not SearchQuery { Term: "old" })
            {
                return null;
            }
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

    [Theory]
    [InlineData("runs", typeof(ListRecentRunReportsQuery))]
    [InlineData("workflows", null)]
    [InlineData("chains", null)]
    [InlineData("logs", typeof(ListRecentRunReportsQuery))]
    [InlineData("traces", null)]
    public async Task Observability_loads_workflow_badge_and_only_the_initial_tabs_other_data(string tab, Type? queryType)
    {
        var dispatcher = new Dispatcher();
        using var telemetry = new JobTelemetryCollector();
        using var vm = new ObservabilityViewModel(Service(dispatcher), new PortalUiState(),
            new Navigation($"http://localhost/observability?tab={tab}"),
            new OperationCenter(null!, default), telemetry, null!, null!);
        await vm.InitializeAsync();
        Assert.Equal(50, Assert.Single(dispatcher.Queries.OfType<ListRecentChainRunsQuery>()).Take);
        var otherQueries = dispatcher.Queries.Where(query => query is not ListRecentChainRunsQuery).ToArray();
        void AssertQuery()
        {
            if (queryType is null)
            {
                Assert.Empty(otherQueries);
                return;
            }
            Assert.IsType(queryType, Assert.Single(otherQueries));
        }
        AssertQuery();
        if (tab == "logs")
        {
            Assert.NotNull(vm.LogResults);
        }
        var count = dispatcher.Queries.Count;
        await vm.RestoreTabAsync(tab);
        Assert.Equal(count, dispatcher.Queries.Count);
    }

    [Fact]
    public async Task Opening_a_live_trace_loads_its_run_without_fetching_job_history()
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
            new OperationCenter(null!, default), telemetry, null!, null!);
        await vm.InitializeAsync();
        await vm.OpenLiveTrace(new(run.Id, run.JobId, "Job", run.ProjectId, run.Status, false,
            run.StartedAt, 100, []));
        Assert.Equal(run.Id, vm.Open?.Run.Id);
        Assert.DoesNotContain(dispatcher.Queries, query => query is ListRecentRunReportsQuery);
        Assert.Single(dispatcher.Queries.OfType<ListRecentChainRunsQuery>());
    }

    [Fact]
    public async Task Observability_workflow_badge_loads_before_slow_job_history_and_updates_on_refresh()
    {
        var dispatcher = new Dispatcher();
        var history = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var workflow = new ChainRunReportView(Guid.NewGuid(), "Project",
            new(Guid.NewGuid(), Guid.NewGuid(), "Workflow", "Succeeded", [], null,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        ChainRunReportView[] workflows = [workflow];
        dispatcher.Delay = query => query switch
        {
            ListRecentRunReportsQuery => history.Task,
            ListRecentChainRunsQuery => Task.FromResult<object>(workflows),
            _ => null
        };
        using var vm = new ObservabilityViewModel(Service(dispatcher), new PortalUiState(), new Navigation(),
            new OperationCenter(null!, default), null!, null!, null!);
        var loading = vm.InitializeAsync();
        Assert.False(vm.Loading);
        Assert.False(loading.IsCompleted);
        Assert.True(vm.IsRunsTab);
        Assert.Equal(workflow, Assert.Single(vm.ChainReports!));
        history.SetResult(Array.Empty<RunReportView>());
        await loading;
        workflows = [workflow, workflow with { Run = workflow.Run with { Id = Guid.NewGuid() } }];
        await vm.RefreshAsync();
        Assert.True(vm.IsRunsTab);
        Assert.Equal(2, vm.ChainReports!.Count);
        var queryCount = dispatcher.Queries.Count;
        await vm.RestoreTabAsync("workflows");
        Assert.Equal(queryCount, dispatcher.Queries.Count);
        Assert.Equal(2, dispatcher.Queries.OfType<ListRecentChainRunsQuery>().Count());
        Assert.Equal(2, dispatcher.Queries.OfType<ListRecentRunReportsQuery>().Count());
        await vm.RestoreTabAsync("runs");
        Assert.Equal(queryCount, dispatcher.Queries.Count);
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

        vm.ShowPane(SidebarPane.Queries);
        vm.ShowPane(SidebarPane.Tables);
        Assert.DoesNotContain(dispatcher.Queries, query => query is ListSearchIndicesQuery);

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
        Assert.DoesNotContain(dispatcher.Queries, query => query is ListSearchIndicesQuery);
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
            if (identifier == "pcmonaco.setSqlSchema")
            {
                SchemaPushed.TrySetResult();
            }
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
        var vm = new JobsViewModel(Service(dispatcher), new OperationCenter(null!, default), null!, new Navigation());
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
        finally
        {
            vm.DetachEvents();
        }
    }

    [Fact]
    public async Task OpenSearch_waits_for_the_chart_action_before_running_aggregations()
    {
        var dispatcher = new Dispatcher();
        var vm = new SearchDataViewModel(Service(dispatcher), new PortalUiState(), null!, new Permissions()) { ProjectId = Guid.NewGuid() };
        await vm.SetParametersAsync();
        Assert.True(vm.InitialLoadComplete);
        Assert.False(vm.ChartsRequested);
        Assert.DoesNotContain(dispatcher.Queries, query => query is SearchDocumentsQuery);
        await vm.SelectIndexAsync(new("records", 1000000, null));
        Assert.DoesNotContain(dispatcher.Queries, query => query is SearchDocumentsQuery);
        await vm.GenerateChartsAsync();
        Assert.True(vm.ChartsRequested);
        Assert.Contains(dispatcher.Queries, query => query is SearchDocumentsQuery);
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
    private static ArtifactsViewModel Artifacts(Dispatcher dispatcher) => new(Service(dispatcher), new PortalUiState(), new Navigation(), null!, null!, null!, null!);
    private static ProjectTablePageResult Page(int page) => new(["name"], [new string?[] { $"row {page}" }], 100, page, 50);

    [Fact]
    public async Task Observability_cancels_running_jobs_once_refreshes_details_and_rechecks_permissions()
    {
        var dispatcher = new Dispatcher();
        var run = new JobRunDetailView(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Running",
            DateTimeOffset.UtcNow, null, [], null, new("code", "python", null, null, null, 1, 1, false));
        var report = new RunReportView(run.JobId, "Test job", "Test project", run);
        dispatcher.Delay = query => query switch
        {
            ListRecentRunReportsQuery => Task.FromResult<object>(new[] { report }),
            GetJobRunQuery => Task.FromResult<object>(run),
            _ => null,
        };
        var permissions = new Permissions { Allowed = true };
        using var vm = new ObservabilityViewModel(Service(dispatcher), new PortalUiState(), new Navigation(),
            new OperationCenter(null!, default), new JobTelemetryCollector(), null!, null!, permissions: permissions);
        await vm.InitializeAsync();
        await vm.OpenRun(report);
        Assert.True(vm.CanCancelRun(run));
        Assert.False(vm.CanCancelRun(run with
        {
            Status = "Succeeded",
            FinishedAt = DateTimeOffset.UtcNow
        }));
        permissions.Allowed = false;
        await vm.CancelRunAsync(run.Id);
        Assert.Empty(dispatcher.Commands);
        Assert.Contains("permission", vm.Message);
        permissions.Allowed = true;
        var pending = new TaskCompletionSource<object>();
        dispatcher.CommandResult = _ => pending.Task;
        var cancelling = vm.CancelRunAsync(run.Id);
        Assert.Equal(run.Id, vm.CancellingRunId);
        await vm.CancelRunAsync(run.Id);
        var command = Assert.IsType<CancelJobRunCommand>(Assert.Single(dispatcher.Commands));
        Assert.Equal(run.Id, command.RunId);
        Assert.Equal(Permission.JobsRun, ((IRequiresPermission)command).RequiredPermission);
        run = run with
        {
            Status = "Cancelled",
            FinishedAt = DateTimeOffset.UtcNow
        };
        report = report with
        {
            Run = run
        };
        pending.SetResult(true);
        await cancelling;
        Assert.Null(vm.CancellingRunId);
        Assert.Equal("Cancelled", vm.Open!.Run.Status);
        Assert.Equal("Cancelled", Assert.Single(vm.Reports!).Run.Status);
        Assert.Equal("Run cancellation requested.", vm.Message);
    }

    private sealed class Dispatcher : IDispatcher
    {
        public List<object> Queries { get; } = [];
        public List<object> Commands { get; } = [];
        public Func<object, Task<object>?>? Delay
        {
            get; set;
        }
        public Func<object, Task<object>>? CommandResult
        {
            get; set;
        }
        private readonly ArtifactFileView[] _files = Enumerable.Range(0, 230).Select(index => new ArtifactFileView(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, "report", $"file {index}", "text/plain", 1, DateTimeOffset.UtcNow)).ToArray();

        public async Task<T> Send<T>(ICommand<T> command, CancellationToken ct = default)
        {
            Commands.Add(command);
            return CommandResult is not null ? (T)await CommandResult(command)
                : (T)(object)new ProjectQueryResult(["name"], [], 0, false);
        }

        public async Task<T> Query<T>(IQuery<T> query, CancellationToken ct = default)
        {
            Queries.Add(query);
            if (Delay?.Invoke(query) is { } pending)
            {
                return (T)await pending;
            }
            object result = query switch
            {
                SearchQuery q => new SearchResultsView(q.Term, []),
                ListDataEntitiesQuery q => new DataEntityView[] { new(Guid.NewGuid(), q.ProjectId, "Records", "records", "name", [], [], DateTimeOffset.UtcNow) },
                QueryProjectTablePageQuery q => Page(q.Page),
                ListProjectDataTablesQuery => Array.Empty<ProjectTableInfo>(),
                ListProjectChartsQuery => new ProjectChartView[] { new("sql:Summary", "{\"type\":\"bar\"}", DateTimeOffset.UtcNow) },
                ListSearchIndicesQuery => Array.Empty<SearchIndexView>(),
                ListSearchDashboardsQuery => Array.Empty<SearchDashboardView>(),
                ListSearchFieldsQuery => new SearchFieldView[] { new("category", "keyword", true, true) },
                GetSearchLastUpdatedQuery => new SearchLastUpdatedView(null, null),
                SearchDocumentsQuery => new SearchResultView(0, 0, [], null),
                GetProjectOverviewQuery q => new ProjectOverviewView(q.ProjectId, "Project", "/project", "Ready", null, null, 0, 0, [], 0),
                GetProjectRequirementsQuery q => RequirementsView.EmptyForProject(q.ProjectId),
                GetTimelineQuery q => new ActivityTimelineView(q.ProjectId, []),
                GetDecisionsQuery => Array.Empty<DecisionView>(),
                ListJobsQuery => Array.Empty<JobView>(),
                ListJobChainsQuery => Array.Empty<JobChainView>(),
                ListRecentRunReportsQuery => Array.Empty<RunReportView>(),
                GetProjectRunCountsQuery => new Dictionary<string, int>(),
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
        public bool Allowed;
        public int ActivityChecks;
        public Task<bool> HasAsync(string permission, CancellationToken ct = default)
        {
            if (permission == Permission.JobsView)
            {
                ActivityChecks++;
            }
            return Task.FromResult(Allowed);
        }
        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(Guid userId, string roleName, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
