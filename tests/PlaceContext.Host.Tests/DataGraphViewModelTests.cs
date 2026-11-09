using Microsoft.JSInterop;
using PlaceContext.Application;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class DataGraphViewModelTests
{
    [Fact]
    public async Task Artifacts_load_on_request_in_pages_and_retry_without_skipping_files()
    {
        var queries = new Queries();
        var vm = Create(queries);
        await vm.LoadAsync(Guid.NewGuid());
        Assert.Empty(queries.ArtifactQueries);
        await vm.LoadArtifactsAsync();
        Assert.Equal(50, vm.Artifacts.Count);
        Assert.True(vm.HasMoreArtifacts);
        Assert.Equal(51, queries.ArtifactQueries[0].Take);
        queries.Fail = true;
        await vm.LoadArtifactsAsync();
        Assert.NotNull(vm.ArtifactsError);
        Assert.Equal(50, vm.Artifacts.Count);
        queries.Fail = false;
        await vm.LoadArtifactsAsync();
        Assert.Null(vm.ArtifactsError);
        Assert.Equal(53, vm.Artifacts.Count);
        Assert.Equal(53, vm.Artifacts.DistinctBy(file => file.Id).Count());
        Assert.False(vm.HasMoreArtifacts);
        await vm.LoadArtifactsAsync();
        Assert.Equal(new[] { 0, 50, 50 }, queries.ArtifactQueries.Select(query => query.Skip));
    }

    [Fact]
    public async Task Artifacts_are_not_requested_without_permission()
    {
        var queries = new Queries();
        var vm = Create(queries, canView: false);
        await vm.LoadAsync(Guid.NewGuid());
        await vm.LoadArtifactsAsync();
        Assert.NotNull(vm.Graph);
        Assert.False(vm.CanViewArtifacts);
        Assert.Empty(queries.ArtifactQueries);
    }

    [Fact]
    public async Task Navigating_away_discards_pending_artifacts_and_their_loading_state()
    {
        var queries = new Queries { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var vm = Create(queries);
        await vm.LoadAsync(Guid.NewGuid());
        var load = vm.LoadArtifactsAsync();
        var nextProject = Guid.NewGuid();
        await vm.LoadAsync(nextProject);
        queries.Pending.SetResult(queries.Files);
        await load;
        Assert.Equal(nextProject, vm.ProjectId);
        Assert.Empty(vm.Artifacts);
        Assert.False(vm.ArtifactsLoading);
        Assert.True(vm.HasMoreArtifacts);
    }

    [Fact]
    public async Task Node_links_open_tables_and_mappings_but_not_jobs()
    {
        var vm = Create(new Queries());
        var projectId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        await vm.LoadAsync(projectId);
        Assert.Equal($"/project/{projectId}/data?table=table%20%26%20view", vm.NodeUrl(new("table:table & view", "Table", 0, false)));
        Assert.Equal($"/project/{projectId}/datamap", vm.NodeUrl(new($"mapping:{Guid.NewGuid():N}", "Mapping", 0, false)));
        Assert.Null(vm.NodeUrl(new($"output:job:{jobId:N}", "Source", 0, false)));
        Assert.Equal($"/project/{projectId}/datamap?job={jobId}", vm.MappingUrl(jobId));
        Assert.Null(vm.NodeUrl(new("unknown", "Unknown", 0, false)));
    }

    private static DataGraphViewModel Create(Queries queries, bool canView = true) =>
        new(new PlaceContextService(queries, null!, null!, null!), new NoJs(), new Permissions(canView));

    private sealed class Queries : IDispatcher
    {
        public List<ListProjectArtifactsQuery> ArtifactQueries { get; } = [];
        public bool Fail { get; set; }
        public TaskCompletionSource<IReadOnlyList<ArtifactFileView>>? Pending { get; set; }
        public IReadOnlyList<ArtifactFileView> Files { get; } = Enumerable.Range(0, 53)
            .Select(i => new ArtifactFileView(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                "RawBundle", $"file-{i}.json", "application/json", 10, DateTimeOffset.UtcNow)).ToArray();
        public async Task<T> Query<T>(IQuery<T> query, CancellationToken ct = default)
        {
            if (query is GetGraphVizQuery graph)
                return (T)(object)new GraphVizView(graph.ProjectId, 0, 0, [], []);
            if (query is ListProjectArtifactsQuery artifacts)
            {
                ArtifactQueries.Add(artifacts);
                if (Fail) throw new InvalidOperationException("Storage unavailable");
                var files = Pending is null ? Files.Skip(artifacts.Skip).Take(artifacts.Take).ToArray() : await Pending.Task;
                return (T)(object)files;
            }
            throw new NotSupportedException(query.GetType().Name);
        }
        public Task<T> Send<T>(ICommand<T> command, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class Permissions(bool canView) : IPermissionService
    {
        public Task<bool> HasAsync(string permission, CancellationToken ct = default)
        {
            Assert.Equal(Permission.ArtifactsView, permission);
            return Task.FromResult(canView);
        }
        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(Guid userId, string roleName, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NoJs : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => ValueTask.FromResult(default(T)!);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken ct, object?[]? args) => ValueTask.FromResult(default(T)!);
    }
}
