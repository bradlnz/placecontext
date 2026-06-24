using PlaceContext.Application.Features;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

public class UsageTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);

    private static async Task<(InMemoryProjectRepository projects, InMemoryUsageRepository usage, Guid projectId)> SeedAsync()
    {
        var projects = new InMemoryProjectRepository();
        var project = Project.Discover(RepoPath.From("/home/brad/code/alpha"), ProjectName.From("alpha"), T0);
        project.Register(T0);
        await projects.AddAsync(project);
        return (projects, new InMemoryUsageRepository(), project.Id.Value);
    }

    [Fact]
    public async Task RecordUsage_persists_and_returns_computed_cost()
    {
        var (projects, usage, projectId) = await SeedAsync();
        var handler = new RecordUsageHandler(projects, usage, new TokenCostCalculator(), new RecordingUnitOfWork(), new FakeClock(T0));

        var view = await handler.HandleAsync(new RecordUsageCommand(projectId, "claude-opus-4-8", 1_000_000, 1_000_000, "feature work"));

        Assert.Equal(90.0m, view.CostUsd); // opus 15 + 75
        Assert.Equal("feature work", view.Description);
        Assert.Single(await usage.ListAllAsync());
    }

    [Fact]
    public async Task RootCost_aggregates_by_model_and_project()
    {
        var (projects, usage, projectId) = await SeedAsync();
        var calc = new TokenCostCalculator();
        var record = new RecordUsageHandler(projects, usage, calc, new RecordingUnitOfWork(), new FakeClock(T0));
        await record.HandleAsync(new RecordUsageCommand(projectId, "claude-sonnet-4-6", 1_000_000, 0, null)); // $3
        await record.HandleAsync(new RecordUsageCommand(projectId, "claude-sonnet-4-6", 1_000_000, 0, null)); // $3
        await record.HandleAsync(new RecordUsageCommand(projectId, "claude-haiku-4-5", 1_000_000, 0, null));  // $1

        var root = await new GetRootCostHandler(usage, projects, calc).HandleAsync(new GetRootCostQuery());

        Assert.Equal(7.0m, root.CostUsd);
        Assert.Equal(3, root.RecordCount);
        Assert.Equal(3_000_000, root.InputTokens);
        Assert.Equal(2, root.ByModel.Count);
        Assert.Equal("claude-sonnet-4-6", root.ByModel[0].Model); // highest cost first
        Assert.Single(root.ByProject);
        Assert.Equal("alpha", root.ByProject[0].Project);
    }
}
