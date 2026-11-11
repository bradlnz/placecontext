using Microsoft.AspNetCore.Components;
using PlaceContext.Application;
using PlaceContext.Application.Access;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class WorkflowRouteTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("list")]
    [InlineData("details")]
    [InlineData("runs")]
    public async Task Loading_the_current_workflow_route_does_not_navigate_again(string? view)
    {
        var chain = Chain();
        var path = Path(chain) + (view is null ? "" : "/" + view);
        var navigation = new Navigation(path);
        var vm = Create(chain, navigation);

        await vm.LoadPageAsync(chain.ProjectId, chain.Id, view: view);
        Assert.True(vm.ShowEditor);
        Assert.Equal(view ?? "canvas", vm.EditorView);
        Assert.Empty(navigation.Visits);

        vm.EdName = "Unsaved draft";
        await vm.LoadPageAsync(chain.ProjectId, chain.Id, view: view);
        Assert.Equal("Unsaved draft", vm.EdName);
        Assert.Empty(navigation.Visits);
    }

    [Fact]
    public async Task Opening_and_closing_a_workflow_navigate_once_to_the_correct_routes()
    {
        var chain = Chain();
        var catalogue = PageRoutes.ProjectWorkflows(chain.ProjectId);
        var navigation = new Navigation(catalogue);
        var vm = Create(chain, navigation);
        await vm.LoadPageAsync(chain.ProjectId, null);

        await vm.OpenChainEditorAsync(chain);
        Assert.Equal(Path(chain), Assert.Single(navigation.Visits));
        await vm.LoadPageAsync(chain.ProjectId, chain.Id);
        Assert.Single(navigation.Visits);

        vm.CloseEditor();
        Assert.False(vm.ShowEditor);
        Assert.Equal(catalogue, navigation.Visits.Last());
        await vm.LoadPageAsync(chain.ProjectId, null);
        Assert.False(vm.ShowEditor);
        Assert.Equal(2, navigation.Visits.Count);
    }

    [Theory]
    [InlineData("canvas")]
    [InlineData("list")]
    public async Task Run_deep_link_loads_without_redirecting_and_can_leave_runs(string view)
    {
        var chain = Chain();
        var run = Run(chain);
        var navigation = new Navigation($"{Path(chain)}/runs/{run.Id}");
        var vm = Create(chain, navigation, run);
        await vm.LoadPageAsync(chain.ProjectId, chain.Id, run.Id, "runs");
        Assert.Equal(run, vm.OpenRun);
        Assert.Empty(navigation.Visits);

        vm.SetEditorView(view);
        var expected = Path(chain) + (view == "canvas" ? "" : "/list");
        Assert.Equal(expected, Assert.Single(navigation.Visits));
        await vm.LoadPageAsync(chain.ProjectId, chain.Id, view: view == "canvas" ? null : view);
        Assert.Equal(view, vm.EditorView);
        Assert.Null(vm.OpenRun);
        Assert.Single(navigation.Visits);
    }

    private static string Path(JobChainView chain) => $"/project/{chain.ProjectId}/workflows/{chain.Id}";

    [Fact]
    public async Task Route_changes_update_a_retained_page_without_reopening_or_redirecting()
    {
        var chain = Chain();
        var run = Run(chain);
        var navigation = new Navigation($"{Path(chain)}/runs/{run.Id}");
        var vm = Create(chain, navigation, run);
        await vm.LoadPageAsync(chain.ProjectId, chain.Id, run.Id, "runs");
        vm.EdName = "Unsaved draft";

        navigation.NavigateTo(Path(chain));
        await vm.LoadPageAsync(chain.ProjectId, chain.Id);
        Assert.Equal("canvas", vm.EditorView);
        Assert.Null(vm.OpenRun);
        Assert.Equal("Unsaved draft", vm.EdName);
        Assert.Single(navigation.Visits);

        navigation.NavigateTo(PageRoutes.ProjectWorkflows(chain.ProjectId));
        await vm.LoadPageAsync(chain.ProjectId, null);
        Assert.False(vm.ShowEditor);
        Assert.Equal(2, navigation.Visits.Count);
    }
    private static JobChainView Chain() => new(Guid.NewGuid(), Guid.NewGuid(), "Workflow", null, [], DateTimeOffset.UtcNow);
    private static ChainRunView Run(JobChainView chain) => new(Guid.NewGuid(), chain.Id, chain.Name, "Succeeded", [], null,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow) { ProjectId = chain.ProjectId };
    private static JobChainsViewModel Create(JobChainView chain, Navigation navigation, ChainRunView? run = null) =>
        new(new PlaceContextService(new Dispatcher(chain, run), null!, null!, null!), null!, new Permissions(), navigation);

    private sealed class Navigation : NavigationManager
    {
        public List<string> Visits { get; } = [];
        public Navigation(string path) => Initialize("http://localhost:7700/", "http://localhost:7700" + path);
        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            Visits.Add(uri);
            Uri = ToAbsoluteUri(uri).AbsoluteUri;
            NotifyLocationChanged(false);
        }
    }

    private sealed class Dispatcher(JobChainView chain, ChainRunView? run) : IDispatcher
    {
        public Task<T> Send<T>(ICommand<T> command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<T> Query<T>(IQuery<T> query, CancellationToken ct = default) => Task.FromResult((T)(query switch
        {
            ListJobChainsQuery => (object)new[] { chain },
            ListJobsQuery => Array.Empty<JobView>(),
            ListChainRunsQuery => run is null ? Array.Empty<ChainRunView>() : new[] { run },
            GetChainRunQuery => run!,
            _ => throw new NotSupportedException(query.GetType().Name)
        }));
    }

    private sealed class Permissions : IPermissionService
    {
        public Task<bool> HasAsync(string permission, CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
        public Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(Guid id, string role, CancellationToken ct = default) => GetEffectivePermissionsAsync(ct);
    }
}
