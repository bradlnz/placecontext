using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Host.Components.Shared;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class WorkflowCodeEditorLinkTests
{
    [Theory]
    [InlineData(true, false, "code", true)]
    [InlineData(false, false, "code", false)]
    [InlineData(true, true, "code", false)]
    [InlineData(true, false, "image", false)]
    [InlineData(false, false, "image", false)]
    [InlineData(true, true, "image", false)]
    public async Task Canvas_code_links_respect_permissions_previews_and_job_kind(
        bool canEdit, bool readOnly, string sourceKind, bool expected)
    {
        var job = Job("Primary") with { MapSourceKind = sourceKind };
        var html = await RenderAsync([new([new(job.Id, job.Name)])], [job], canEdit, readOnly);
        Assert.Equal(expected, html.Contains("Edit code for", StringComparison.Ordinal));
        Assert.Equal(canEdit && !readOnly, html.Contains("job-settings-link", StringComparison.Ordinal));
        if (canEdit && !readOnly)
            Assert.Contains($"href=\"{PageRoutes.ProjectJobs(job.ProjectId)}?job={job.Id}\"", html);
        if (expected)
        {
            Assert.Contains(PageRoutes.ProjectJob(job.ProjectId, job.Id), html);
            Assert.Contains("target=\"_blank\"", html);
            Assert.Contains("rel=\"noopener noreferrer\"", html);
            Assert.Contains("opens in a new tab", html);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Conditional_and_nested_false_path_jobs_open_their_own_code_editor(bool embedded)
    {
        var jobs = new[] { Job("Primary"), Job("Fallback"), Job("Nested fallback") };
        var nested = new JobChainStageView([new(jobs[2].Id, jobs[2].Name)]);
        var fallback = new JobChainStageView([new(jobs[1].Id, jobs[1].Name)],
            new ConditionGateView("eq:result:ok"), [nested]);
        var stage = new JobChainStageView([new(jobs[0].Id, jobs[0].Name)],
            new ConditionGateView("eq:result:ok"), [fallback]);
        var html = await RenderAsync([stage], jobs, true, false, embedded);
        foreach (var job in jobs)
        {
            if (embedded)
                Assert.Contains($"aria-label=\"Edit code for {job.Name}\"", html);
            else
                Assert.Contains($"href=\"{PageRoutes.ProjectJob(job.ProjectId, job.Id)}\"", html);
        }
        if (embedded)
            foreach (var job in jobs) Assert.DoesNotContain($"href=\"{PageRoutes.ProjectJob(job.ProjectId, job.Id)}\"", html);
    }

    private static async Task<string> RenderAsync(IReadOnlyList<JobChainStageView> stages,
        IReadOnlyList<JobView> jobs, bool canEdit, bool readOnly, bool embedded = false)
    {
        var services = new ServiceCollection().AddLogging().AddTransient<ChainCanvasViewModel>();
        services.AddAuthorizationCore(options => options.AddPolicy(Permission.JobsEdit,
            policy => policy.RequireClaim("permission", Permission.JobsEdit)));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var user = new ClaimsPrincipal(new ClaimsIdentity(canEdit
            ? [new Claim("permission", Permission.JobsEdit)] : Array.Empty<Claim>(), "test"));
        RenderFragment canvas = builder =>
        {
            builder.OpenComponent<ChainCanvas>(0);
            builder.AddAttribute(1, "Stages", stages);
            builder.AddAttribute(2, "Jobs", jobs);
            builder.AddAttribute(3, "ReadOnly", readOnly);
            if (embedded)
                builder.AddAttribute(4, "OnEditCode", EventCallback.Factory.Create<JobView>(jobs, _ => { }));
            builder.CloseComponent();
        };
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<CascadingValue<Task<AuthenticationState>>>(
                ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    ["Value"] = Task.FromResult(new AuthenticationState(user)),
                    ["ChildContent"] = canvas
                }));
            return rendered.ToHtmlString();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pending_permission_checks_keep_controls_stable_only_in_the_same_project(bool changeProject)
    {
        var authorization = new DelayedAuthorization();
        await using var provider = new ServiceCollection().AddLogging().AddAuthorizationCore(options =>
                options.AddPolicy(Permission.JobsEdit, policy => policy.RequireAuthenticatedUser()))
            .AddSingleton<IAuthorizationService>(authorization).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var job = Job("Primary");
        JobCodeLink links = null!;
        RenderFragment content = builder =>
        {
            builder.OpenComponent<JobCodeLink>(0);
            builder.AddAttribute(1, "Job", job);
            builder.AddComponentReferenceCapture(2, component => links = (JobCodeLink)component);
            builder.CloseComponent();
        };
        var root = await renderer.Dispatcher.InvokeAsync(() =>
            renderer.RenderComponentAsync<CascadingValue<Task<AuthenticationState>>>(
                ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    ["Value"] = Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity("test")))),
                    ["ChildContent"] = content
                })));
        Assert.Contains("job-settings-link", await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));

        async Task<Task> Update()
        {
            Task rendering = Task.CompletedTask;
            await renderer.Dispatcher.InvokeAsync(() =>
            {
                rendering = links.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Job"] = job }));
            });
            return rendering;
        }
        var pending = new TaskCompletionSource<AuthorizationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        authorization.Result = pending.Task;
        job = job with { Name = "Updated", ProjectId = changeProject ? Guid.NewGuid() : job.ProjectId };
        var update = await Update();
        try
        {
            var html = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
            Assert.Equal(!changeProject, html.Contains("job-settings-link", StringComparison.Ordinal));
        }
        finally
        {
            pending.SetResult(AuthorizationResult.Failed());
            await update;
        }
        Assert.DoesNotContain("job-settings-link", await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));

        var older = new TaskCompletionSource<AuthorizationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        authorization.Result = older.Task;
        var oldUpdate = await Update();
        authorization.Result = Task.FromResult(AuthorizationResult.Failed());
        await (await Update());
        older.SetResult(AuthorizationResult.Success());
        await oldUpdate;
        Assert.DoesNotContain("job-settings-link", await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
    }

    private sealed class DelayedAuthorization : IAuthorizationService
    {
        public Task<AuthorizationResult> Result { get; set; } = Task.FromResult(AuthorizationResult.Success());
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) => Result;
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) => Result;
    }

    private static JobView Job(string name) => new(
        Id: Guid.NewGuid(), ProjectId: Guid.NewGuid(), Name: name, Description: null,
        MapSourceKind: "code", MapImage: null, MapRuntimeId: "node", MapSource: "", MapEntrypoint: "index.js",
        MapFiles: [], ShardCount: 1, InputPayloads: [], MapEnv: new Dictionary<string, string>(),
        ReduceSourceKind: null, ReduceImage: null, ReduceRuntimeId: null, ReduceSource: null,
        ReduceEntrypoint: null, ReduceFiles: [], ReduceEnv: null, ConcurrencyLimit: 1,
        SuccessExitCodes: [0], PartialExitCodes: [], AllowNetworkEgress: false, AllowApiInvocation: false,
        Parameters: [], PostJobActions: [], ReturnType: JobReturnType.Json, ReturnFileName: null,
        RetryCount: 0, RetryDelaySeconds: 0, McpConnectionIds: [],
        CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow);
}
