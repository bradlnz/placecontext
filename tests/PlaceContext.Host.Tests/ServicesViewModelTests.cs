using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class ServicesViewModelTests
{
    [Fact]
    public async Task Service_catalogue_loads_database_choices_only_when_the_editor_opens()
    {
        var databases = new DatabaseStub();
        var vm = new ServicesViewModel(new ConsoleStub(), new(), databases);
        await vm.LoadAsync(Guid.NewGuid());
        Assert.Equal(0, databases.ListCalls);
        await vm.NewService();
        Assert.Equal(1, databases.ListCalls);
        Assert.True(vm.ShowEditor);
    }

    [Fact]
    public async Task App_details_remain_selected_when_logs_are_unavailable_and_clear_on_return_to_list()
    {
        var app = new ServiceInstance(Guid.NewGuid(), "Demo", "image", "running", "ready", null, DateTimeOffset.UtcNow);
        var api = new ConsoleStub { List = _ => Task.FromResult<IReadOnlyList<ServiceInstance>>([app]), ReadLogs = _ => throw new InvalidOperationException("Logs unavailable") };
        var vm = new ServicesViewModel(api, new());
        var project = Guid.NewGuid();
        await vm.LoadAsync(project, app.Id);
        Assert.Equal(app, vm.Selected);
        Assert.Equal(app.Id, vm.DetailServiceId);
        Assert.Equal("Logs unavailable", vm.Error);
        Assert.Equal($"/project/{project}/services/{app.Id}", vm.ServiceUrl(app.Id));
        await vm.LoadAsync(project, app.Id, "databases");
        Assert.Equal(app, vm.Selected);
        Assert.Null(vm.Error);
        Assert.Null(vm.Inspecting);
        await vm.LoadAsync(project);
        Assert.Null(vm.DetailServiceId);
        Assert.Null(vm.Selected);
        Assert.Null(vm.Inspecting);
        Assert.Null(vm.Error);
        Assert.Empty(vm.Logs);
    }

    [Fact]
    public async Task Switching_apps_during_a_fetch_only_loads_the_current_apps_logs()
    {
        var first = new ServiceInstance(Guid.NewGuid(), "First", "image", "running", "ready", null, DateTimeOffset.UtcNow);
        var second = first with { Id = Guid.NewGuid(), Name = "Second" };
        var waiting = new TaskCompletionSource<IReadOnlyList<ServiceInstance>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var logRequests = new List<Guid>();
        var api = new ConsoleStub { List = _ => waiting.Task, ReadLogs = id => { logRequests.Add(id); return Task.FromResult("Current logs"); } };
        var vm = new ServicesViewModel(api, new());
        var project = Guid.NewGuid();
        var loadingFirst = vm.LoadAsync(project, first.Id);
        var loadingSecond = vm.LoadAsync(project, second.Id);
        waiting.SetResult([first, second]);
        await Task.WhenAll(loadingFirst, loadingSecond);
        Assert.Equal(second, vm.Selected);
        Assert.Equal(second, vm.Inspecting);
        Assert.Equal(second.Id, Assert.Single(logRequests));
        Assert.Equal("Current logs", vm.Logs);
        await vm.LoadAsync(project, Guid.NewGuid());
        Assert.Null(vm.Selected);
        Assert.Null(vm.Inspecting);
        Assert.Empty(vm.Logs);
    }

    [Fact]
    public async Task Switching_projects_during_a_fetch_never_shows_the_previous_projects_services()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var waiting = new TaskCompletionSource<IReadOnlyList<ServiceInstance>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new ConsoleStub { List = project => project == first ? waiting.Task : Task.FromResult<IReadOnlyList<ServiceInstance>>([]) };
        var vm = new ServicesViewModel(api, new());
        var loadingFirst = vm.LoadAsync(first);
        var loadingSecond = vm.LoadAsync(second);
        waiting.SetResult([new(Guid.NewGuid(), "Private portal", "image", "running", null, 30001, DateTimeOffset.UtcNow)]);
        await Task.WhenAll(loadingFirst, loadingSecond);
        Assert.Equal(second, vm.ProjectId);
        Assert.Empty(vm.Services);
        Assert.False(vm.Busy);
    }

    [Fact]
    public async Task Worker_launch_has_no_port_clears_secrets_after_success_and_retains_failed_forms()
    {
        var api = new ConsoleStub();
        var vm = new ServicesViewModel(api, new());
        await vm.LoadAsync(Guid.NewGuid());
        await vm.NewService();
        vm.Name = "Worker"; vm.Image = "image"; vm.Kind = "worker"; vm.Environment = "TOKEN=secret";
        await vm.LaunchAsync();
        Assert.Null(api.Launched!.ContainerPort);
        Assert.Equal("TOKEN=secret", api.Launched.Environment);
        Assert.Empty(vm.Environment);
        Assert.False(vm.ShowEditor);
        await vm.NewService(); api.Fail = true;
        await vm.LaunchAsync();
        Assert.True(vm.ShowEditor);
        Assert.Contains("unavailable", vm.Error);
        Assert.False(vm.Busy);
    }

    private sealed class ConsoleStub : IServiceConsole
    {
        public Func<Guid, Task<IReadOnlyList<ServiceInstance>>> List { get; init; } = _ => Task.FromResult<IReadOnlyList<ServiceInstance>>([]);
        public ServiceLaunch? Launched { get; private set; }
        public bool Fail { get; set; }
        public Func<Guid, Task<string>> ReadLogs { get; init; } = _ => Task.FromResult("");
        public Task<IReadOnlyList<ServiceInstance>> ListAsync(Guid projectId, CancellationToken ct = default) => List(projectId);
        public Task<ServiceCode?> GetCodeAsync(Guid projectId, Guid serviceId, CancellationToken ct = default) => Task.FromResult<ServiceCode?>(null);
        public Task UpdateCodeAsync(Guid projectId, Guid serviceId, ServiceCode code, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Guid> LaunchAsync(Guid projectId, ServiceLaunch service, CancellationToken ct = default)
        { if (Fail) throw new InvalidOperationException("Docker unavailable"); Launched = service; return Task.FromResult(Guid.NewGuid()); }
        public Task ControlAsync(Guid projectId, Guid serviceId, string action, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> LogsAsync(Guid projectId, Guid serviceId, CancellationToken ct = default) => ReadLogs(serviceId);
        public Task<Uri> GetSsoEndpointAsync(Guid projectId, Guid serviceId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class DatabaseStub : IDatabaseConsole
    {
        public int ListCalls;
        public Task<IReadOnlyList<DatabaseInstance>> ListAsync(Guid projectId, CancellationToken ct = default)
        { ListCalls++; return Task.FromResult<IReadOnlyList<DatabaseInstance>>([]); }
        public Task CreateAsync(Guid projectId, DatabaseLaunch database, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ControlAsync(Guid projectId, Guid databaseId, string action, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> LogsAsync(Guid projectId, Guid databaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<DatabaseQueryResult> QueryAsync(Guid projectId, Guid databaseId, string query, string? cursor = null, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
