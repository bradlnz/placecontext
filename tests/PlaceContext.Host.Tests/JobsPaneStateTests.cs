using Microsoft.AspNetCore.Components;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Operations;
using PlaceContext.Application;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class JobsPaneStateTests
{
    [Fact]
    public async Task Closing_a_run_ignores_an_inflight_detail_response()
    {
        var dispatcher = new DelayedDispatcher();
        var vm = new JobsViewModel(new PlaceContextService(dispatcher, null!, null!, null!),
            new OperationCenter(null!, default), null!, new Navigation());
        var opening = vm.OpenRunDetailAsync(Guid.NewGuid());
        vm.CloseRunDetail();
        dispatcher.Detail.SetResult(new JobRunDetailView(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Succeeded",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [], null, new("code", "python", null, null, null, 1, 1, false)));
        await opening;
        Assert.Null(vm.RunDetail);
        Assert.Null(vm.RunArtifacts);
        vm.DetachEvents();
    }

    [Fact]
    public void Closing_editor_removes_the_deep_link_and_leaving_clears_transient_panes()
    {
        var navigation = new Navigation();
        var vm = new JobsViewModel(null!, new OperationCenter(null!, default), null!, navigation);
        vm.NewJob();
        Assert.True(vm.ShowEditor);
        vm.CloseEditor();
        Assert.False(vm.ShowEditor);
        Assert.DoesNotContain("job=", navigation.Uri);
        Assert.DoesNotContain("tab=", navigation.Uri);
        Assert.Contains("keep=yes", navigation.Uri);
        Assert.True(navigation.Replaced);
        vm.OpenTemplateModal();
        Assert.True(vm.ShowTemplateModal);
        vm.DetachEvents();
        Assert.False(vm.ShowTemplateModal);
        Assert.False(vm.ShowEditor);
        Assert.Null(vm.SelectedJobId);
    }

    private sealed class DelayedDispatcher : IDispatcher
    {
        public TaskCompletionSource<JobRunDetailView?> Detail { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<T> Send<T>(ICommand<T> command, CancellationToken ct = default) => throw new NotSupportedException();
        public async Task<T> Query<T>(IQuery<T> query, CancellationToken ct = default)
        {
            if (query is GetJobRunQuery)
            {
                return (T)(object)(await Detail.Task)!;
            }
            if (typeof(T) == typeof(IReadOnlyList<RunArtifactLinkView>))
            {
                return (T)(object)Array.Empty<RunArtifactLinkView>();
            }
            throw new NotSupportedException();
        }
    }

    private sealed class Navigation : NavigationManager
    {
        public bool Replaced;
        public Navigation() => Initialize("http://localhost/", "http://localhost/project/11111111-1111-1111-1111-111111111111/jobs?job=22222222-2222-2222-2222-222222222222&tab=triggers&keep=yes");
        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            Replaced = options.ReplaceHistoryEntry;
            Uri = ToAbsoluteUri(uri).ToString();
        }
    }
}
