using Microsoft.AspNetCore.Components;
using PlaceContext.Application;
using PlaceContext.Application.Access;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class SubWorkflowEditingTests
{
    [Fact]
    public async Task New_branch_workflow_keeps_its_id_and_updates_the_same_workflow_on_later_saves()
    {
        var store = new Store();
        var navigation = new Navigation();
        var vm = Editor(store, navigation, embedded: true);
        await vm.LoadPageAsync(store.Parent.ProjectId, null);
        vm.NewChain("New branch", [ChainStage.ForAction(new QueryDataChainAction("SELECT 3", "result"))]);
        await vm.SaveChainAsync(closeEditor: false);
        Assert.Null(vm.EditorError);
        Assert.Equal(store.Child.Id, vm.EditChainId);
        Assert.True(vm.ShowEditor);
        var id = vm.EditChainId;
        vm.EdName = "Renamed branch";
        await vm.SaveChainAsync();
        Assert.Null(vm.EditorError);
        Assert.Equal(id, vm.EditChainId);
        Assert.Equal("Renamed branch", store.Child.Name);
        Assert.False(vm.ShowEditor);
        Assert.Equal(2, store.Saves);
        Assert.Equal("http://localhost/", navigation.Uri);
    }

    [Fact]
    public async Task Saving_child_refreshes_preview_without_changing_parent_draft_or_route()
    {
        var store = new Store();
        var navigation = new Navigation();
        var parent = Editor(store, navigation);
        await parent.LoadPageAsync(store.Parent.ProjectId, store.Parent.Id);
        parent.EdName = "Unsaved parent name";
        parent.EdStagePolicies[0] = new(2, 5, 60);
        parent.OpenSubWorkflowAction(0, true);
        var parentRoute = navigation.Uri;
        var child = Editor(store, navigation, embedded: true);
        Assert.IsAssignableFrom<IComponentViewModel>(child);
        await child.LoadPageAsync(store.Child.ProjectId, store.Child.Id);
        child.SetEditorView("list");
        child.EdName = "Edited child";
        child.EdStageActions[0] = new QueryDataChainAction("SELECT 2", "result");
        await child.SaveChainAsync();
        Assert.Null(child.EditorError);
        Assert.False(child.ShowEditor);
        await parent.RefreshSubWorkflowAsync();

        Assert.Equal(parentRoute, navigation.Uri);
        Assert.Equal(store.Parent.Id, parent.EditChainId);
        Assert.Equal("Unsaved parent name", parent.EdName);
        Assert.Equal(2, parent.EdStagePolicies[0]!.MaxRetries);
        Assert.True(parent.ShowSubWorkflowEditor);
        Assert.Equal("Edited child", parent.SelectedSubWorkflow!.Name);
        Assert.Equal("SELECT 2", Assert.IsType<QueryDataChainActionView>(parent.SelectedSubWorkflow.Stages[0].Action).Sql);
        parent.SaveSubWorkflowAction();
        Assert.Equal("Edited child", Assert.IsType<SubWorkflowChainAction>(parent.EdStageActions[0]).WorkflowName);
        Assert.Equal("Parent", store.Parent.Name);
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public async Task Cancelling_child_leaves_saved_definition_and_parent_selection_unchanged()
    {
        var store = new Store();
        var navigation = new Navigation();
        var parent = Editor(store, navigation);
        await parent.LoadPageAsync(store.Parent.ProjectId, store.Parent.Id);
        parent.OpenSubWorkflowAction(0, true);
        var route = navigation.Uri;
        var child = Editor(store, navigation, embedded: true);
        await child.LoadPageAsync(store.Child.ProjectId, store.Child.Id);
        child.EdName = "Discard this";
        child.CloseEditor();
        await parent.RefreshSubWorkflowAsync();
        Assert.Equal(route, navigation.Uri);
        Assert.Equal("Child", parent.SelectedSubWorkflow!.Name);
        Assert.Equal(store.Child.Id.ToString(), parent.SubWorkflowId);
        Assert.True(parent.ShowEditor);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task Failed_child_save_keeps_editor_and_draft_open()
    {
        var store = new Store();
        var child = Editor(store, new Navigation(), embedded: true);
        await child.LoadPageAsync(store.Child.ProjectId, store.Child.Id);
        child.EdName = "Draft";
        store.Child = store.Child with { UpdatedAt = store.Child.UpdatedAt.AddSeconds(1) };
        await child.SaveChainAsync();
        Assert.Contains("newer revision", child.EditorError);
        Assert.True(child.ShowEditor);
        Assert.Equal("Draft", child.EdName);
        Assert.Equal(0, store.Saves);
    }

    private static JobChainsViewModel Editor(Store store, Navigation navigation, bool embedded = false) =>
        new(new PlaceContextService(store, null!, null!, null!), null!, new Permissions(), navigation) { Embedded = embedded };

    private sealed class Navigation : NavigationManager
    {
        public Navigation() => Initialize("http://localhost/", "http://localhost/");
        protected override void NavigateToCore(string uri, NavigationOptions options) => Uri = ToAbsoluteUri(uri).AbsoluteUri;
    }

    private sealed class Permissions : IPermissionService
    {
        public Task<bool> HasAsync(string permission, CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
        public Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(Guid id, string role, CancellationToken ct = default) => GetEffectivePermissionsAsync(ct);
    }

    private sealed class Store : IDispatcher
    {
        public JobChainView Child = new(Guid.NewGuid(), Guid.NewGuid(), "Child", null,
            [new([], Action: new QueryDataChainActionView("SELECT 1", "result"))], DateTimeOffset.UtcNow);
        public JobChainView Parent;
        public int Saves;
        public Store() => Parent = new(Guid.NewGuid(), Child.ProjectId, "Parent", null,
            [new([], Action: new SubWorkflowChainActionView(Child.Id, Child.Name))], DateTimeOffset.UtcNow);

        public Task<T> Query<T>(IQuery<T> query, CancellationToken ct = default) => Task.FromResult((T)(object)(query switch
        {
            ListJobChainsQuery => (object)new[] { Parent, Child },
            ListJobsQuery => Array.Empty<JobView>(),
            ListChainRunsQuery => Array.Empty<ChainRunView>(),
            _ => throw new NotSupportedException(query.GetType().Name)
        }));

        public Task<T> Send<T>(ICommand<T> command, CancellationToken ct = default)
        {
            if (command is CreateJobChainCommand create)
            {
                var query = Assert.IsType<QueryDataChainAction>(create.StageActions![0]);
                Child = new(Guid.NewGuid(), create.ProjectId, create.Name, null,
                    [new([], Action: new QueryDataChainActionView(query.Sql, query.OutputField))], DateTimeOffset.UtcNow);
                Saves++;
                return Task.FromResult((T)(object)Child);
            }
            var update = Assert.IsType<UpdateJobChainCommand>(command);
            Assert.Equal(Child.Id, update.ChainId);
            if (update.ExpectedUpdatedAt != Child.UpdatedAt) throw new InvalidOperationException("A newer revision exists.");
            var action = Assert.IsType<QueryDataChainAction>(update.StageActions![0]);
            Child = Child with { Name = update.Name, Stages = [new([], Action: new QueryDataChainActionView(action.Sql, action.OutputField))], UpdatedAt = Child.UpdatedAt.AddSeconds(1) };
            Saves++;
            return Task.FromResult((T)(object)Child);
        }
    }
}
