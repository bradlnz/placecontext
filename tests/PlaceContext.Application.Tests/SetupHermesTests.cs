using PlaceContext.Application.Features;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

/// <summary>
/// The hermes skill: one MCP call scaffolds a job-orchestration playbook into the project's
/// .claude/skills — the skill must walk an agent through the real MCP tool surface in the right
/// order (authoring guide before upload, upload before run, runs before artifacts, triggers last).
/// </summary>
public class SetupHermesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 9, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Setup_writes_the_hermes_skill_with_the_orchestration_tool_surface()
    {
        var projects = new InMemoryProjectRepository();
        var project = Project.Discover(ProjectPath.From("/repo/demo"), ProjectName.From("demo"), T0);
        await projects.AddAsync(project);
        var scaffolder = new FakeSkillScaffolder();
        var handler = new SetupHermesHandler(projects, scaffolder);

        var view = await handler.HandleAsync(new SetupHermesCommand(project.Id.Value));

        Assert.Equal("hermes", view.Name);
        Assert.EndsWith(".claude/skills/hermes/SKILL.md", view.Path.Replace('\\', '/'));

        var md = scaffolder.LastMarkdown!;
        Assert.Contains("name: hermes", md);
        Assert.Contains(project.Id.Value.ToString(), md);           // the agent needs the project id
        // The orchestration loop, in dependency order:
        foreach (var tool in new[]
        {
            "job_authoring_guide", "upload_job_code", "run_job",
            "list_job_runs", "get_job_run", "create_trigger", "emit_event",
        })
            Assert.Contains(tool, md);
        // The contract's sharp edge must be stated: read the guide BEFORE writing code.
        Assert.Contains("BEFORE", md);
    }

    [Fact]
    public async Task Setup_rejects_an_unknown_project()
    {
        var handler = new SetupHermesHandler(new InMemoryProjectRepository(), new FakeSkillScaffolder());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new SetupHermesCommand(Guid.NewGuid())));
    }
}
