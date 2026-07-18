using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Services;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

public class OnboardTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);

    private static (OnboardHandler handler, InMemoryProjectRepository projects, InMemoryActivityLogRepository ledgers, FakeRepoFiles files)
        Build(FakeGitPort git, FakeRepoFiles files)
    {
        var projects = new InMemoryProjectRepository();
        var ledgers = new InMemoryActivityLogRepository();
        var clock = new FakeClock(T0);
        var handler = new OnboardHandler(
            projects, ledgers, git,
            new InMemoryRequirementsRepository(), files, new RecordingUnitOfWork(), clock);
        return (handler, projects, ledgers, files);
    }

    private static FakeGitPort GitWithTwoCommits() => new()
    {
        Commits = new[]
        {
            new CommitInfo("abc1230", "Ann", "Add webhook", T0, new[] { "a.cs" }),
            new CommitInfo("def4560", "Bob", "Fix bug", T0, new[] { "b.cs" }),
        }
    };

    [Fact]
    public async Task Onboard_creates_backfills_and_scaffolds_claude()
    {
        var files = new FakeRepoFiles();
        var (handler, projects, ledgers, _) = Build(GitWithTwoCommits(), files);

        var result = await handler.HandleAsync(new OnboardCommand("/home/brad/code/payments", null, "claude", 50));

        Assert.Equal("payments", result.Project.Name);
        Assert.Equal(2, result.ChangesBackfilled);
        Assert.Single(result.SkillsCreated);
        Assert.Single(result.AgentsCreated);
        Assert.Contains(files.Written, w => w.Path == ".claude/skills/placecontext/SKILL.md");
        Assert.Contains(files.Written, w => w.Path == ".claude/agents/reviewer.md");

        var project = (await projects.ListAsync()).Single();
        var ledger = await ledgers.GetForProjectAsync(project.Id);
        Assert.Equal(2, ledger.Records.Count);
        Assert.True(ledger.Records[0].IsCommitted); // oldest first, commit attached
    }

    [Fact]
    public async Task Onboard_codex_writes_codex_paths()
    {
        var files = new FakeRepoFiles();
        var (handler, _, _, _) = Build(GitWithTwoCommits(), files);

        await handler.HandleAsync(new OnboardCommand("/home/brad/code/payments", null, "codex", 50));

        Assert.Contains(files.Written, w => w.Path == ".codex/prompts/placecontext.md");
        Assert.Contains(files.Written, w => w.Path == ".codex/prompts/reviewer.md");
    }

    [Fact]
    public async Task Onboard_is_idempotent_and_does_not_duplicate_backfill()
    {
        var files = new FakeRepoFiles();
        var (handler, _, ledgers, _) = Build(GitWithTwoCommits(), files);
        var cmd = new OnboardCommand("/home/brad/code/payments", null, "claude", 50);

        var first = await handler.HandleAsync(cmd);
        var second = await handler.HandleAsync(cmd);

        Assert.Equal(2, first.ChangesBackfilled);
        Assert.Equal(0, second.ChangesBackfilled); // already recorded
    }
}
