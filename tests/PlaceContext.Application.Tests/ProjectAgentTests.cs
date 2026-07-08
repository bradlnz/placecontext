using PlaceContext.Application.Features;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

/// <summary>
/// The per-project agent: reviews project state and queues "what to do next" as work items —
/// LLM-driven when a gateway is configured, deterministic signals otherwise, and never the same
/// suggestion twice.
/// </summary>
public class ProjectAgentTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 8, 10, 0, 0, TimeSpan.Zero);

    private sealed record World(
        ProjectAgentService Agent,
        Project Project,
        InMemoryWorkItemRepository WorkItems,
        FakeLlmGateway Llm);

    private static async Task<World> BuildAsync(bool llmEnabled, string? llmReply = null, int failedRuns = 0)
    {
        var projects = new InMemoryProjectRepository();
        var activity = new InMemoryActivityLogRepository();
        var workItems = new InMemoryWorkItemRepository();
        var runs = new InMemoryJobRunRepository();
        var llm = new FakeLlmGateway(llmEnabled) { Reply = llmReply };
        var project = Project.Discover(ProjectPath.From("/repo/demo"), ProjectName.From("demo"), T0);
        await projects.AddAsync(project);

        var mapSpec = new MapSpec("img", new[] { "{}" }, new Dictionary<string, string>());
        for (var i = 0; i < failedRuns; i++)
        {
            var run = JobRun.Start(Guid.NewGuid(), project.Id.Value, T0.AddMinutes(i), WorkloadSnapshot.From(mapSpec, null, 1));
            run.Complete(new[] { new ShardResult(0, 1, WorkloadOutcome.Failed, null, "boom") }, null, T0.AddMinutes(i + 1));
            await runs.AddAsync(run);
        }

        var agent = new ProjectAgentService(
            projects, activity, workItems, runs, llm, new RecordingUnitOfWork(), new FakeClock(T0));
        return new World(agent, project, workItems, llm);
    }

    [Fact]
    public async Task Llm_next_steps_become_work_items()
    {
        var w = await BuildAsync(llmEnabled: true, llmReply:
            """
            {"assessment":"The project is idle.","nextSteps":[
              {"title":"Wire the booking summary job to a schedule","detail":"So artifacts flow daily.","priority":"High"},
              {"title":"Add a reduce step","detail":"Aggregate shards.","priority":"Normal"}]}
            """);

        var result = await w.Agent.RunAsync(w.Project.Id.Value);

        Assert.NotNull(result);
        Assert.Equal("The project is idle.", result!.Assessment);
        var items = await w.WorkItems.ListForProjectAsync(w.Project.Id);
        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i.Title == "Wire the booking summary job to a schedule" && i.Priority == WorkItemPriority.High);
        Assert.All(items, i => Assert.Contains("project agent", i.Detail!));
    }

    [Fact]
    public async Task A_second_pass_does_not_repeat_open_suggestions()
    {
        var reply = """{"assessment":"same","nextSteps":[{"title":"Do the thing","priority":"Normal"}]}""";
        var w = await BuildAsync(llmEnabled: true, llmReply: reply);

        await w.Agent.RunAsync(w.Project.Id.Value);
        var second = await w.Agent.RunAsync(w.Project.Id.Value);

        Assert.Empty(second!.QueuedTitles);
        Assert.Single(await w.WorkItems.ListForProjectAsync(w.Project.Id));
    }

    [Fact]
    public async Task Without_an_llm_failed_runs_surface_deterministically()
    {
        var w = await BuildAsync(llmEnabled: false, failedRuns: 2);

        var result = await w.Agent.RunAsync(w.Project.Id.Value);

        var items = await w.WorkItems.ListForProjectAsync(w.Project.Id);
        Assert.Contains(items, i => i.Title.Contains("failed job run") && i.Priority == WorkItemPriority.High);
        Assert.Contains("deterministic pass", result!.Assessment);
    }

    [Fact]
    public async Task Garbled_llm_output_falls_back_instead_of_failing()
    {
        var w = await BuildAsync(llmEnabled: true, llmReply: "sorry, as a language model I cannot", failedRuns: 1);

        var result = await w.Agent.RunAsync(w.Project.Id.Value);

        Assert.NotNull(result);
        Assert.Contains("deterministic pass", result!.Assessment);
    }

    [Fact]
    public async Task Unknown_project_returns_null()
    {
        var w = await BuildAsync(llmEnabled: false);
        Assert.Null(await w.Agent.RunAsync(Guid.NewGuid()));
    }
}
