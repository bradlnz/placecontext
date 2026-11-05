using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlaceContext.Application;
using PlaceContext.Application.Access;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Host.Components.Pages;
using PlaceContext.Host.Components.ViewModels;

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
        var service = new PlaceContextService(new Dispatcher(chain), null!, null!, null!);
        var navigation = new Navigation();
        var vm = new JobChainsViewModel(service, null!, new Permissions(), navigation);
        await using var provider = new ServiceCollection().AddLogging()
            .AddSingleton(vm).AddSingleton<NavigationManager>(navigation).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<JobChains>(ParameterView.FromDictionary(new Dictionary<string, object?>
                { ["ProjectId"] = projectId }))).ToHtmlString());
        Assert.Contains("Build and verify", html);
        Assert.Contains("Agent task", html);
        Assert.Contains("Repeat flow", html);
    }

    private sealed class Dispatcher(JobChainView chain) : IDispatcher
    {
        public Task<T> Send<T>(ICommand<T> command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<T> Query<T>(IQuery<T> query, CancellationToken ct = default) => Task.FromResult((T)(query switch
        {
            ListJobChainsQuery => (object)new[] { chain },
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
