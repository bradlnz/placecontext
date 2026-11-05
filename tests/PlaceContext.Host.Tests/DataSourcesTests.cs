using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using PlaceContext.Application.Activity;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataSources;
using PlaceContext.Application.Features;
using PlaceContext.Application.Jobs;
using PlaceContext.Host.Auth;
using PlaceContext.Host.Components.ViewModels;
using PlaceContext.Host.Tools;
using PlaceContext.TestSupport;

namespace PlaceContext.Host.Tests;

public sealed class DataSourcesTests
{
    [Fact]
    public void Source_picker_omits_email_but_existing_email_sources_remain_supported()
    {
        var vm = new DataSourcesViewModel(new Queries(), new PortalUiState(), null!, null!);
        Assert.Equal(9, vm.ConnectableProviders.Count);
        Assert.DoesNotContain(vm.ConnectableProviders, provider => provider.Id == "email");
        Assert.Contains(vm.ConnectableProviders, provider => provider.Id == "feed");
        Assert.Equal("email", SourceProviders.Get("email").Id);
    }

    [Fact]
    public async Task Navigation_does_not_show_a_previous_projects_pending_search()
    {
        var dispatcher = new Queries();
        var vm = new DataSourcesViewModel(dispatcher, new PortalUiState(), null!, null!);
        await vm.InitializeAsync(Guid.NewGuid());
        vm.SelectedSourceId = Guid.NewGuid();
        vm.FieldsApproved = true;
        vm.ConnectionString = "private";
        var pending = vm.SearchAsync();
        await vm.InitializeAsync(Guid.NewGuid());
        dispatcher.Search.SetResult(new("keyword", [Hit()]));
        await pending;
        Assert.Null(vm.SearchResult);
        Assert.Empty(vm.ConnectionString);
        Assert.False(vm.FieldsApproved);
        Assert.False(vm.Busy);
    }

    [Fact]
    public async Task Preview_preserves_records_and_selection_changes_clear_old_evidence()
    {
        var dispatcher = new Queries();
        var projectId = Guid.NewGuid();
        var first = new DataSourceDefinition(Guid.NewGuid(), projectId, "Notes", "public", "notes", "id", ["id", "body", "optional"], [], false, Guid.NewGuid());
        dispatcher.Sources = [first, first with { Id = Guid.NewGuid(), Name = "Other" }];
        dispatcher.Records = [new("1", "{\"body\":\"<script>untrusted</script>\",\"id\":1,\"optional\":null}")];
        var vm = new DataSourcesViewModel(dispatcher, new PortalUiState(), null!, null!);
        await vm.InitializeAsync(projectId);
        await vm.PreviewAsync(first.Id);
        Assert.Equal(dispatcher.Records, vm.Preview);
        vm.SelectSource(dispatcher.Sources[1].Id);
        Assert.Null(vm.Preview);
        Assert.Null(vm.SearchResult);
        vm.OpenConnect();
        vm.ConnectionString = "private";
        vm.FieldsApproved = true;
        vm.CloseConnect();
        Assert.False(vm.ShowConnect);
        Assert.Empty(vm.ConnectionString);
        Assert.False(vm.FieldsApproved);
    }

    [Fact]
    public async Task Refresh_keeps_previous_preview_and_search_visible_until_replacement_arrives()
    {
        var dispatcher = new Queries();
        var projectId = Guid.NewGuid();
        var source = new DataSourceDefinition(Guid.NewGuid(), projectId, "Notes", "public", "notes", "id", ["id", "body"], [], false, Guid.NewGuid());
        dispatcher.Sources = [source];
        dispatcher.Records = [new("1", "{\"id\":1,\"body\":\"kiwi\"}")];
        var vm = new DataSourcesViewModel(dispatcher, new PortalUiState(), null!, null!);
        await vm.InitializeAsync(projectId);
        await vm.PreviewAsync(source.Id);
        var preview = vm.Preview;
        dispatcher.PendingRecords = new();
        var refresh = vm.PreviewAsync(source.Id);
        Assert.True(vm.Busy);
        Assert.Same(preview, vm.Preview);
        dispatcher.PendingRecords.SetResult([new("2", "{\"id\":2,\"body\":\"pear\"}")]);
        await refresh;
        Assert.Contains("pear", Assert.Single(vm.Preview!).Json);
        dispatcher.Search.SetResult(new("keyword", [Hit()]));
        await vm.SearchAsync();
        var search = vm.SearchResult;
        dispatcher.Search = new();
        var pendingSearch = vm.SearchAsync();
        Assert.True(vm.Busy);
        Assert.Same(search, vm.SearchResult);
        dispatcher.Search.SetResult(new("keyword", []));
        await pendingSearch;
        Assert.Empty(vm.SearchResult!.Hits);
    }

    [Fact]
    public void Source_sync_requires_a_valid_job_identity_and_explicit_source_grant()
    {
        var tenant = new FakeCurrentTenant(Guid.NewGuid());
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([], JobDataAuthenticationHandler.SchemeName));
        var grant = new JobDataGrant(tenant.TenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), clock.UtcNow.AddHours(1));
        context.Features.Set(grant);
        var access = new HttpJobDataAccess(new HttpContextAccessor { HttpContext = context }, tenant, clock);
        Assert.Equal(grant.ProjectId, access.RequireProjectId());
        Assert.Throws<UnauthorizedAccessException>(() => access.RequireSourceId());
        var sourceId = Guid.NewGuid();
        context.Features.Set(grant with { SourceId = sourceId });
        Assert.Equal(sourceId, access.RequireSourceId());
        context.User = new ClaimsPrincipal(new ClaimsIdentity([], "other"));
        Assert.Throws<UnauthorizedAccessException>(() => access.RequireSourceId());
    }

    [Theory]
    [InlineData("list")]
    [InlineData("records")]
    [InlineData("search")]
    [InlineData("search-all")]
    public async Task Source_tool_traces_omit_retrieved_content_and_queries(string tool)
    {
        var dispatcher = new Queries();
        dispatcher.Search.SetResult(new("keyword", [Hit()]));
        var log = new Log();
        var project = Guid.NewGuid();
        var source = Guid.NewGuid();
        var result = tool switch
        {
            "list" => await PlaceContextTools.ListDataSources(dispatcher, log, project),
            "records" => await PlaceContextTools.ReadSourceRecords(dispatcher, log, project, source),
            "search-all" => await PlaceContextTools.SearchDataSources(dispatcher, log, project, "private-marker"),
            _ => await PlaceContextTools.SearchDataSource(dispatcher, log, project, source, "private-marker")
        };
        Assert.DoesNotContain("private-marker", JsonSerializer.Serialize(Assert.Single(log.Entries)));
        if (tool != "list")
        {
            Assert.Contains("private-marker", result);
        }
    }

    [Fact]
    public async Task Failed_save_preserves_setup_so_the_user_can_correct_and_retry()
    {
        var dispatcher = new Queries { RejectCreate = true };
        var vm = new DataSourcesViewModel(dispatcher, new PortalUiState(), null!, null!);
        await vm.InitializeAsync(Guid.NewGuid());
        vm.OpenConnect("feed");
        vm.FieldsApproved = true;
        vm.OpenStep(3);
        vm.QueueId = Guid.NewGuid();
        vm.ConnectionString = "connection-to-keep-until-success";
        await vm.CreateAsync();
        Assert.Equal("Choose an enabled queue.", vm.Error);
        Assert.True(vm.CanSave);
        Assert.True(vm.ShowConnect);
        Assert.NotEmpty(vm.Datasets);
        Assert.Equal("connection-to-keep-until-success", vm.ConnectionString);
        dispatcher.RejectCreate = false;
        await vm.CreateAsync();
        Assert.NotNull(dispatcher.Created);
        Assert.False(vm.ShowConnect);
        Assert.Empty(vm.ConnectionString);
        Assert.Empty(vm.Datasets);
    }

    [Fact]
    public async Task Setup_creates_a_source_without_model_processing_and_requires_field_approval()
    {
        var dispatcher = new Queries();
        var vm = new DataSourcesViewModel(dispatcher, new PortalUiState(), null!, null!);
        await vm.InitializeAsync(Guid.NewGuid());
        vm.OpenConnect("email");
        Assert.Equal(3, vm.SetupTiles.Count);
        Assert.True(vm.CanOpenStep(2));
        Assert.False(vm.CanOpenStep(3));
        vm.OpenStep(2);
        await vm.CreateAsync();
        Assert.Null(dispatcher.Created);
        vm.FieldsApproved = true;
        vm.ContinueSetup();
        Assert.Equal(3, vm.SetupStep);
        Assert.False(vm.CanSave);
        vm.QueueId = Guid.NewGuid();
        vm.RequiredFields = "text";
        vm.RedactedFields = "from";
        Assert.True(vm.CanSave);
        await vm.CreateAsync();
        Assert.NotNull(dispatcher.Created);
        Assert.Null(dispatcher.Created.Processing);
        Assert.False(dispatcher.Created.ProcessingApproved);
        Assert.Equal(vm.QueueId, dispatcher.Created.Queue!.QueueId);
        Assert.Equal(["text"], dispatcher.Created.Queue.RequiredFields);
        Assert.Equal(["from"], dispatcher.Created.Queue.RedactedFields);
        Assert.False(vm.ShowConnect);
        Assert.NotNull(vm.FeedToken);
        vm.OpenConnect("email");
        vm.FieldsApproved = true;
        vm.OpenStep(3);
        vm.SelectDataset(0);
        Assert.False(vm.FieldsApproved);
        Assert.False(vm.CanSave);
    }

    [Theory]
    [InlineData("running", "running", 0)]
    [InlineData("running", "interrupted", -11)]
    [InlineData("needs-review", "needs-review", 0)]
    [InlineData("completed", "completed", 0)]
    [InlineData("pending", "pending", 0)]
    public async Task Import_status_reflects_persisted_runs(string status, string expected, int startedMinutesAgo)
    {
        var projectId = Guid.NewGuid();
        var source = new DataSourceDefinition(Guid.NewGuid(), projectId, "Notes", "public", "notes", "id", ["id"], [], false, Guid.NewGuid(),
            SyncedAt: DateTimeOffset.UtcNow, RecordCount: 1, Execution: new(Guid.NewGuid(), status, "Processing", DateTimeOffset.UtcNow.AddMinutes(startedMinutesAgo), Held: 1));
        var vm = new DataSourcesViewModel(new Queries { Sources = [source] }, new PortalUiState(), null!, null!);
        await vm.InitializeAsync(projectId);
        Assert.Equal(expected, vm.ExecutionStatus);
    }

    [Fact]
    public async Task Status_polling_does_not_redraw_unchanged_sources_but_applies_new_versions()
    {
        var projectId = Guid.NewGuid();
        var source = new DataSourceDefinition(Guid.NewGuid(), projectId, "Notes", "public", "notes", "id", ["id"], [], false, Guid.NewGuid());
        var dispatcher = new Queries { Sources = [source] };
        var vm = new DataSourcesViewModel(dispatcher, new PortalUiState(), null!, null!);
        await vm.InitializeAsync(projectId);
        var original = vm.Sources;
        dispatcher.Sources = [source with { Columns = source.Columns.ToArray() }];
        var polledTwice = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        var renders = 0;
        dispatcher.Listed = () =>
        {
            if (Interlocked.Increment(ref reads) == 2)
            {
                polledTwice.TrySetResult();
            }
        };
        vm.Attach(() =>
        {
            Interlocked.Increment(ref renders);
            changed.TrySetResult();
            return Task.CompletedTask;
        });
        vm.StartStatusPolling();
        try
        {
            await polledTwice.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, renders);
            Assert.Same(original, vm.Sources);
            dispatcher.Sources = [source with { Version = Guid.NewGuid(), RecordCount = 1 }];
            await changed.Task.WaitAsync(TimeSpan.FromSeconds(6));
            Assert.Equal(1, renders);
            Assert.Equal(1, vm.SelectedSource!.RecordCount);
        }
        finally
        {
            vm.StopStatusPolling();
            vm.Detach();
        }
    }

    [Fact]
    public async Task Delete_confirmation_can_be_cancelled_and_success_clears_feed_credentials()
    {
        var dispatcher = new Queries();
        var vm = new DataSourcesViewModel(dispatcher, new PortalUiState(), null!, null!);
        await vm.InitializeAsync(Guid.NewGuid());
        vm.OpenConnect("email");
        vm.FieldsApproved = true;
        vm.OpenStep(3);
        vm.QueueId = Guid.NewGuid();
        await vm.CreateAsync();
        var source = Assert.Single(vm.Sources);
        Assert.NotNull(vm.FeedToken);
        vm.RequestDelete(source.Id);
        Assert.Equal(source.Id, vm.PendingDelete);
        Assert.Null(dispatcher.Deleted);
        vm.CancelDelete();
        Assert.Null(vm.PendingDelete);
        Assert.Single(vm.Sources);
        vm.RequestDelete(source.Id);
        await vm.DeleteAsync(source.Id);
        Assert.Equal(new DeleteDataSourceCommand(source.ProjectId, source.Id), dispatcher.Deleted);
        Assert.Empty(vm.Sources);
        Assert.Equal(Guid.Empty, vm.SelectedSourceId);
        Assert.Null(vm.PendingDelete);
        Assert.Null(vm.FeedToken);
        Assert.Null(vm.FeedSourceId);
    }

    [Theory]
    [InlineData(" https://workers.example.test/portal/ ", "https://workers.example.test/portal/")]
    [InlineData("", "https://browser.example.test/portal/")]
    [InlineData("   ", "https://browser.example.test/portal/")]
    [InlineData(null, "https://browser.example.test/portal/")]
    public async Task Creating_a_workflow_uses_the_configured_portal_or_current_base_address(string? configured, string expected)
    {
        var dispatcher = new Queries();
        var configuration = new ConfigurationManager { ["PlaceContext:PublicBaseUrl"] = configured };
        var vm = new DataSourcesViewModel(dispatcher, new PortalUiState(), configuration, new Navigation());
        var projectId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        await vm.InitializeAsync(projectId);

        await vm.CreateWorkflowAsync(sourceId);

        Assert.Equal(new CreateSourceWorkflowCommand(projectId, sourceId, expected), dispatcher.Workflow);
        Assert.Equal($"/project/{projectId}/workflows?chain={dispatcher.WorkflowId}", vm.WorkflowUrl);
        Assert.False(vm.Busy);
    }

    private sealed class Navigation : NavigationManager
    {
        public Navigation() => Initialize("https://browser.example.test/portal/", "https://browser.example.test/portal/project/sources");
    }

    private static DataSourceHit Hit() => new("citation", Guid.NewGuid(), "source", "public.notes", "1", 0, "private-marker", [], 1, DateTimeOffset.UtcNow);
    private sealed class Log : IToolCallLog
    {
        public List<ToolCallEntry> Entries { get; } = [];
        public void Record(ToolCallEntry entry) => Entries.Add(entry);
        public IReadOnlyList<ToolCallEntry> Recent(int take = 100) => Entries.Take(take).ToArray();
    }
    private sealed class Queries : IDispatcher
    {
        public IReadOnlyList<DataSourceDefinition> Sources { get; set; } = [];
        public IReadOnlyList<SourceRecord> Records { get; set; } = [new("1", "private-marker")];
        public TaskCompletionSource<DataSourceSearchResult> Search { get; set; } = new();
        public TaskCompletionSource<IReadOnlyList<SourceRecord>>? PendingRecords { get; set; }
        public Action? Listed { get; set; }
        public async Task<T> Query<T>(IQuery<T> query, CancellationToken ct = default)
        {
            if (query is ListDataSourcesQuery)
            {
                Listed?.Invoke();
            }
            object result = query switch
            {
                ListDataSourcesQuery => Sources,
                ReadSourceRecordsQuery => PendingRecords is null ? Records : await PendingRecords.Task,
                SearchDataSourceQuery => await Search.Task,
                SearchProjectSourcesQuery => new DataSourceSearchResult[] { await Search.Task },
                _ => throw new NotSupportedException()
            };
            return (T)result;
        }
        public Guid WorkflowId { get; } = Guid.NewGuid();
        public CreateSourceWorkflowCommand? Workflow { get; private set; }
        public bool RejectCreate { get; set; }
        public CreateDataSourceCommand? Created { get; private set; }
        public DeleteDataSourceCommand? Deleted { get; private set; }
        public Task<T> Send<T>(ICommand<T> command, CancellationToken ct = default)
        {
            if (command is CreateSourceWorkflowCommand workflow)
            {
                Workflow = workflow;
                return Task.FromResult((T)(object)WorkflowId);
            }
            if (command is DeleteDataSourceCommand delete)
            {
                Deleted = delete;
                Sources = Sources.Where(source => source.Id != delete.SourceId).ToArray();
                return Task.FromResult((T)(object)true);
            }
            if (command is not CreateDataSourceCommand create)
            {
                throw new NotSupportedException();
            }
            if (RejectCreate)
            {
                throw new ArgumentException("Choose an enabled queue.");
            }
            Created = create;
            var source = new DataSourceDefinition(Guid.NewGuid(), create.ProjectId, create.Name, create.Schema, create.Table,
                create.KeyColumn, create.Columns, create.Tags, create.EnableEmbeddings, Guid.NewGuid(), Provider: create.Provider, Processing: create.Processing);
            Sources = [source];
            return Task.FromResult((T)(object)source);
        }
    }
}
