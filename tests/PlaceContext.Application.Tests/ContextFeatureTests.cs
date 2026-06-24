using PlaceContext.Application.Features;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

public class ContextFeatureTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AddContext_then_GetContext_round_trips_markdown()
    {
        var contexts = new InMemoryProjectContextRepository();
        var pid = Guid.NewGuid();
        var add = new AddContextHandler(contexts, new RecordingUnitOfWork(), new FakeClock(T0));

        await add.HandleAsync(new AddContextCommand(pid, "# Goals\nShip it."));
        await add.HandleAsync(new AddContextCommand(pid, "# Gotchas\nWatch the cache."));

        var get = new GetContextHandler(contexts);
        var view = await get.HandleAsync(new GetContextQuery(pid));

        Assert.False(view.IsEmpty);
        Assert.Contains("# Goals", view.Markdown);
        Assert.Contains("# Gotchas", view.Markdown);
    }

    [Fact]
    public async Task GetContext_returns_empty_for_unknown_project()
    {
        var view = await new GetContextHandler(new InMemoryProjectContextRepository())
            .HandleAsync(new GetContextQuery(Guid.NewGuid()));
        Assert.True(view.IsEmpty);
    }

    [Fact]
    public async Task SetContext_replaces_the_whole_document()
    {
        var contexts = new InMemoryProjectContextRepository();
        var pid = Guid.NewGuid();
        await new AddContextHandler(contexts, new RecordingUnitOfWork(), new FakeClock(T0))
            .HandleAsync(new AddContextCommand(pid, "# Old\nstale notes."));

        var set = new SetContextHandler(contexts, new RecordingUnitOfWork(), new FakeClock(T0));
        var view = await set.HandleAsync(new SetContextCommand(pid, "# Fresh\nthe whole truth."));

        Assert.False(view.IsEmpty);
        Assert.Contains("# Fresh", view.Markdown);
        Assert.DoesNotContain("# Old", view.Markdown);
    }

    [Fact]
    public async Task SetContext_creates_the_document_when_absent()
    {
        var contexts = new InMemoryProjectContextRepository();
        var pid = Guid.NewGuid();

        var view = await new SetContextHandler(contexts, new RecordingUnitOfWork(), new FakeClock(T0))
            .HandleAsync(new SetContextCommand(pid, "# Hello"));

        Assert.False(view.IsEmpty);
        Assert.Contains("# Hello", view.Markdown);
    }

    [Fact]
    public async Task SuggestImprovements_flags_hotspots_unverified_and_missing_context()
    {
        var projectId = Guid.NewGuid();
        var pid = ProjectId.From(projectId);

        var tree = DecisionTree.Of(
            new[]
            {
                new DecisionTreeNode("root", "alpha", TreeNodeKind.Root, 1, false),
                new DecisionTreeNode("file:core.cs", "core.cs", TreeNodeKind.File, 6, true),
            },
            new[] { new DecisionTreeEdge("root", "file:core.cs", ConfidenceTag.Extracted) });

        var ledgers = new InMemoryChangeLedgerRepository();
        var ledger = ChangeLedger.Start(pid);
        ledger.Append("sloppy", Author.Agent("claude"), Rationale.None, TestDelta.None,
            DebtDelta.None, ChangeVerification.None, new[] { "core.cs" }, Array.Empty<GraphNodeId>(), T0);
        await ledgers.SaveAsync(ledger);

        var handler = new SuggestImprovementsHandler(
            new FakeDecisionTreeProvider { Tree = tree }, ledgers,
            new InMemoryProjectContextRepository(), new InMemoryDebtAssessmentRepository());

        var view = await handler.HandleAsync(new SuggestImprovementsQuery(projectId));
        var codes = view.Items.Select(i => i.Code).ToList();

        Assert.Contains("churn-hotspot", codes);
        Assert.Contains("unverified-changes", codes);
        Assert.Contains("missing-context", codes);
    }

    [Fact]
    public async Task ScaffoldSkill_writes_seeded_skill_markdown()
    {
        var projects = new InMemoryProjectRepository();
        var project = Project.Discover(RepoPath.From("/home/brad/code/alpha"), ProjectName.From("alpha"), T0);
        project.Register(T0);
        await projects.AddAsync(project);

        var decisions = new InMemoryDecisionRepository();
        await decisions.AddAsync(Decision.Record(project.Id, "Use EF Core?", "Yes, code-first", Rationale.None, T0));

        var scaffolder = new FakeSkillScaffolder();
        var handler = new ScaffoldSkillHandler(projects, decisions, new InMemoryProjectContextRepository(), scaffolder);

        var view = await handler.HandleAsync(new ScaffoldSkillCommand(project.Id.Value, "Alpha Helper", "Helps with alpha."));

        Assert.EndsWith("/SKILL.md", view.Path);
        Assert.Contains("name: alpha-helper", view.Markdown);
        Assert.Contains("Use EF Core?", view.Markdown); // seeded from the recorded decision
        Assert.Equal(view.Markdown, scaffolder.LastMarkdown);
    }
}
