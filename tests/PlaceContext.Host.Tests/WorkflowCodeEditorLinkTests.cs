using System.Security.Claims;
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
    public async Task Canvas_code_links_respect_permissions_previews_and_job_kind(
        bool canEdit, bool readOnly, string sourceKind, bool expected)
    {
        var job = Job("Primary") with { MapSourceKind = sourceKind };
        var html = await RenderAsync([new([new(job.Id, job.Name)])], [job], canEdit, readOnly);
        Assert.Equal(expected, html.Contains("job-code-link", StringComparison.Ordinal));
        if (expected)
        {
            Assert.Contains(PageRoutes.ProjectJob(job.ProjectId, job.Id), html);
            Assert.Contains("target=\"_blank\"", html);
            Assert.Contains("rel=\"noopener noreferrer\"", html);
            Assert.Contains("opens in a new tab", html);
        }
    }

    [Fact]
    public async Task Conditional_and_nested_false_path_jobs_open_their_own_code_editor()
    {
        var jobs = new[] { Job("Primary"), Job("Fallback"), Job("Nested fallback") };
        var nested = new JobChainStageView([new(jobs[2].Id, jobs[2].Name)]);
        var fallback = new JobChainStageView([new(jobs[1].Id, jobs[1].Name)],
            new ConditionGateView("eq:result:ok"), [nested]);
        var stage = new JobChainStageView([new(jobs[0].Id, jobs[0].Name)],
            new ConditionGateView("eq:result:ok"), [fallback]);
        var html = await RenderAsync([stage], jobs, true, false);
        foreach (var job in jobs)
        {
            Assert.Contains($"href=\"{PageRoutes.ProjectJob(job.ProjectId, job.Id)}\"", html);
        }
    }

    private static async Task<string> RenderAsync(IReadOnlyList<JobChainStageView> stages,
        IReadOnlyList<JobView> jobs, bool canEdit, bool readOnly)
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
