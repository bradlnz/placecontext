using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class WorkflowRunCountTests
{
    [Fact]
    public async Task Opening_workflow_loads_runs_before_selecting_the_runs_tab()
    {
        var chain = Chain();
        var runs = new[] { Run(chain), Run(chain) };
        var vm = CreateVm(new Dispatcher(_ => Task.FromResult<IReadOnlyList<ChainRunView>>(runs)));

        await vm.OpenChainEditorAsync(chain);

        Assert.True(vm.IsDetailsTab);
        Assert.Equal(2, vm.ChainRuns?.Count);
        Assert.Null(vm.OpenRun);
    }

    [Fact]
    public async Task Runs_open_full_screen_and_returning_to_canvas_preserves_the_draft()
    {
        var chain = Chain();
        var vm = CreateVm(new Dispatcher(_ => Task.FromResult<IReadOnlyList<ChainRunView>>([Run(chain)])));
        await vm.OpenChainEditorAsync(chain);
        Assert.True(vm.IsCanvasView);
        vm.EdName = "Unsaved workflow name";
        await vm.ShowWorkflowRunsAsync();
        Assert.True(vm.IsRunsView);
        Assert.Single(vm.ChainRuns!);
        vm.EditorView = "list";
        Assert.True(vm.IsListView);
        vm.EditorView = "canvas";
        Assert.Equal("Unsaved workflow name", vm.EdName);
    }

    [Fact]
    public async Task Switching_workflows_clears_the_count_and_ignores_the_previous_response()
    {
        var first = Chain();
        var second = Chain();
        var firstResponse = new TaskCompletionSource<IReadOnlyList<ChainRunView>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondResponse = new TaskCompletionSource<IReadOnlyList<ChainRunView>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = CreateVm(new Dispatcher(query => query.ChainId == first.Id ? firstResponse.Task : secondResponse.Task));
        var openingFirst = vm.OpenChainEditorAsync(first);
        var openingSecond = vm.OpenChainEditorAsync(second);

        firstResponse.SetResult([Run(first), Run(first)]);
        await openingFirst;
        Assert.Null(vm.ChainRuns);

        secondResponse.SetResult([Run(second)]);
        await openingSecond;
        Assert.Equal(second.Id, Assert.Single(vm.ChainRuns!).ChainId);
        Assert.True(vm.IsDetailsTab);

        await vm.OpenChainEditorAsync(first);
        Assert.Equal(2, vm.ChainRuns?.Count);
        var reopeningSecond = vm.OpenChainEditorAsync(second);
        await reopeningSecond;
        Assert.Equal(second.Id, Assert.Single(vm.ChainRuns!).ChainId);
    }

    private static JobChainView Chain() => new(Guid.NewGuid(), Guid.NewGuid(), "Workflow", null, [], DateTimeOffset.UtcNow);

    private static ChainRunView Run(JobChainView chain) =>
        new(Guid.NewGuid(), chain.Id, chain.Name, "Succeeded", [], null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static JobChainsViewModel CreateVm(IDispatcher dispatcher) =>
        new(new PlaceContextService(dispatcher, null!, null!, null!), null!, null!, null!);

    private sealed class Dispatcher(Func<ListChainRunsQuery, Task<IReadOnlyList<ChainRunView>>> runs) : IDispatcher
    {
        public Task<T> Send<T>(ICommand<T> command, CancellationToken ct = default) => throw new NotSupportedException();

        public async Task<T> Query<T>(IQuery<T> query, CancellationToken ct = default) =>
            query is ListChainRunsQuery list ? (T)(object)await runs(list) : throw new NotSupportedException();
    }
}
