using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlaceContext.Application;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Host.Components.Pages;
using PlaceContext.Host.Components.ViewModels;
using PlaceContext.Application.Access;
using PlaceContext.Application.Access.Models;

namespace PlaceContext.Host.Tests;

public sealed class WorkflowListRenderingTests
{
    [Fact]
    public async Task Action_only_stages_and_empty_stage_do_not_crash_workflow_catalogue()
    {
        var projectId = Guid.NewGuid();
        var chain = new JobChainView(Guid.NewGuid(), projectId, "Build and verify", null,
            [new([], Action: new LoopChainActionView(LoopChainAction.TaskType, "Define requirements", "requirements")),
             new([], Action: new RepeatFlowChainActionView("eq:ready:true", 3)), new([])], DateTimeOffset.UtcNow);
        var (html, _) = await RenderAsync(projectId, chain);
        Assert.Contains("Build and verify", html);
        Assert.Contains("Agent task", html);
        Assert.Contains("Repeat flow", html);
    }

    [Fact]
    public async Task Catalogue_nests_direct_parallel_and_else_workflows_and_shows_shared_links()
    {
        var projectId = Guid.NewGuid();
        JobChainView Workflow(string name, params JobChainStageView[] stages) =>
            new(Guid.NewGuid(), projectId, name, null, stages, DateTimeOffset.UtcNow);
        JobChainStageView Link(JobChainView workflow) => new([], Action: new SubWorkflowChainActionView(workflow.Id, workflow.Name));
        var grandchild = Workflow("Grandchild");
        var child = Workflow("Child", new JobChainStageView([], ElseBranch: [new([], ElseBranch: [Link(grandchild)])]));
        var parallel = Workflow("Parallel child");
        var parent = Workflow("Parent", Link(child), Link(child), new([], Action: new ParallelWorkflowsChainActionView(
            [new(child.Id, child.Name, "eq:ready:true"), new(parallel.Id, parallel.Name, "eq:ready:true")])));
        var otherParent = Workflow("Other parent", Link(child));
        var standalone = Workflow("Standalone");
        var (html, vm) = await RenderAsync(projectId, child, grandchild, parent, parallel, otherParent, standalone);
        var roots = vm.WorkflowHierarchy;
        Assert.Equal(new[] { parent.Id, otherParent.Id, standalone.Id }, roots.Select(node => node.Workflow.Id));
        Assert.Equal(new[] { child.Id, parallel.Id }, roots[0].Children.Select(node => node.Workflow.Id));
        Assert.Equal(grandchild.Id, Assert.Single(roots[0].Children[0].Children).Workflow.Id);
        var shared = Assert.Single(roots[1].Children);
        Assert.Equal(child.Id, shared.Workflow.Id);
        Assert.True(shared.IsReference);
        Assert.Empty(shared.Children);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, $"data-workflow-id=\"{child.Id}\"").Count);
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(html, "<ul class=\"chain-rows\"").Count);
        Assert.Contains("aria-label=\"Open Child\"", html);
        Assert.Contains("Linked workflow", html);
    }

    [Fact]
    public async Task Cycles_and_unavailable_children_do_not_hide_workflows_or_recurse_forever()
    {
        var projectId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var first = new JobChainView(firstId, projectId, "First", null,
            [new([], Action: new SubWorkflowChainActionView(secondId, "Second")),
             new([], Action: new SubWorkflowChainActionView(Guid.NewGuid(), "Unavailable"))], DateTimeOffset.UtcNow);
        var second = new JobChainView(secondId, projectId, "Second", null,
            [new([], Action: new SubWorkflowChainActionView(firstId, "First"))], DateTimeOffset.UtcNow);
        var (_, vm) = await RenderAsync(projectId, first, second);
        var root = Assert.Single(vm.WorkflowHierarchy);
        Assert.Equal(firstId, root.Workflow.Id);
        var child = Assert.Single(root.Children);
        Assert.Equal(secondId, child.Workflow.Id);
        var reference = Assert.Single(child.Children);
        Assert.Equal(firstId, reference.Workflow.Id);
        Assert.True(reference.IsReference);
        Assert.Empty(reference.Children);
    }

    private static async Task<(string Html, JobChainsViewModel Vm)> RenderAsync(Guid projectId, params JobChainView[] chains)
    {
        var service = new PlaceContextService(new Dispatcher(chains), null!, null!, null!);
        var navigation = new Navigation();
        var vm = new JobChainsViewModel(service, null!, new Permissions(), navigation);
        await using var provider = new ServiceCollection().AddLogging().AddAuthorizationCore(options =>
            {
                foreach (var permission in new[] { Permission.ChainsManage, Permission.TriggersManage, Permission.ProjectsView })
                    options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser());
            })
            .AddSingleton(vm).AddSingleton(new AssistantPageViewModel(new PortalUiState()))
            .AddSingleton<NavigationManager>(navigation).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        RenderFragment content = builder =>
        {
            builder.OpenComponent<JobChains>(0);
            builder.AddAttribute(1, "ProjectId", projectId);
            builder.CloseComponent();
        };
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<CascadingValue<Task<AuthenticationState>>>(ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    ["Value"] = Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity("test")))),
                    ["ChildContent"] = content
                }))).ToHtmlString());
        return (html, vm);
    }

    private sealed class Dispatcher(JobChainView[] chains) : IDispatcher
    {
        public Task<T> Send<T>(ICommand<T> command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<T> Query<T>(IQuery<T> query, CancellationToken ct = default) => Task.FromResult((T)(query switch
        {
            ListJobChainsQuery => (object)chains,
            ListJobsQuery => Array.Empty<JobView>(),
            _ => throw new NotSupportedException(query.GetType().Name)
        }));
    }
    private sealed class Navigation : NavigationManager
    {
        public Navigation() => Initialize("http://localhost/", "http://localhost/");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }
    private sealed class Permissions : IPermissionService
    {
        public Task<bool> HasAsync(string permission, CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
        public Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(Guid id, string role, CancellationToken ct = default) => GetEffectivePermissionsAsync(ct);
    }
}
