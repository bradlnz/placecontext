using PlaceContext.Application.Features;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

public class CodeRequirementsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Effective_requirements_merge_global_then_project()
    {
        var repo = new InMemoryCodeRequirementsRepository();
        var uow = new RecordingUnitOfWork();
        var clock = new FakeClock(T0);
        var projectId = Guid.NewGuid();

        await new SetGlobalRequirementsHandler(repo, uow, clock)
            .HandleAsync(new SetGlobalRequirementsCommand("All code must have tests."));
        await new SetProjectRequirementsHandler(repo, uow, clock)
            .HandleAsync(new SetProjectRequirementsCommand(projectId, "Use the repository pattern."));

        var effective = await new GetEffectiveRequirementsHandler(repo)
            .HandleAsync(new GetEffectiveRequirementsQuery(projectId));

        Assert.False(effective.IsEmpty);
        Assert.Contains("# Global requirements", effective.Markdown);
        Assert.Contains("All code must have tests.", effective.Markdown);
        Assert.Contains("# Project requirements", effective.Markdown);
        Assert.Contains("Use the repository pattern.", effective.Markdown);
        // Global appears before project.
        Assert.True(effective.Markdown.IndexOf("Global", StringComparison.Ordinal)
                  < effective.Markdown.IndexOf("Project", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Effective_requirements_are_empty_when_none_defined()
    {
        var repo = new InMemoryCodeRequirementsRepository();
        var effective = await new GetEffectiveRequirementsHandler(repo)
            .HandleAsync(new GetEffectiveRequirementsQuery(Guid.NewGuid()));

        Assert.True(effective.IsEmpty);
        Assert.Equal(string.Empty, effective.Markdown);
    }

    [Fact]
    public async Task SetGlobal_then_GetGlobal_round_trips_and_is_marked_global()
    {
        var repo = new InMemoryCodeRequirementsRepository();
        await new SetGlobalRequirementsHandler(repo, new RecordingUnitOfWork(), new FakeClock(T0))
            .HandleAsync(new SetGlobalRequirementsCommand("# Standards\nNo secrets in code."));

        var view = await new GetGlobalRequirementsHandler(repo).HandleAsync(new GetGlobalRequirementsQuery());

        Assert.True(view.IsGlobal);
        Assert.Null(view.ProjectId);
        Assert.Contains("No secrets in code.", view.Markdown);
    }
}
