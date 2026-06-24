using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

public class HandlerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 23, 12, 0, 0, TimeSpan.Zero);

    private static DebtAssessmentService Debt(IClock clock, IDebtAssessmentRepository assessments) => new(
        new InMemoryChangeLedgerRepository(), assessments, new FakeDecisionTreeProvider(),
        new FakeCodeMetricsProbe(), new FakeDebtCalculatorFactory(), new DebtScoreCalculator(), clock);

    [Fact]
    public async Task CreateProject_registers_a_new_project_and_assesses_its_debt()
    {
        var repo = new InMemoryProjectRepository();
        var uow = new RecordingUnitOfWork();
        var assessments = new InMemoryDebtAssessmentRepository();
        var handler = new CreateProjectHandler(repo, Debt(new FakeClock(T0), assessments), uow, new FakeClock(T0));

        var view = await handler.HandleAsync(new CreateProjectCommand("/home/brad/code/alpha", null));

        Assert.Equal("alpha", view.Name); // name defaults to the last path segment
        Assert.Equal(nameof(ProjectStatus.Registered), view.Status);
        Assert.Equal(2, uow.SaveCount); // persist the project, then its initial debt assessment
        Assert.Single(await repo.ListAsync());

        // Debt is computed as part of creation: an assessment snapshot exists and scores are applied.
        var project = (await repo.ListAsync()).Single();
        Assert.NotNull(await assessments.GetLatestAsync(project.Id));
        Assert.NotNull(view.TechnicalDebt);
        Assert.NotNull(view.AgenticDebt);
    }

    [Fact]
    public async Task CreateProject_is_idempotent_for_known_paths()
    {
        var repo = new InMemoryProjectRepository();
        var handler = new CreateProjectHandler(
            repo, Debt(new FakeClock(T0), new InMemoryDebtAssessmentRepository()), new RecordingUnitOfWork(), new FakeClock(T0));

        await handler.HandleAsync(new CreateProjectCommand("/home/brad/code/alpha", "alpha"));
        await handler.HandleAsync(new CreateProjectCommand("/home/brad/code/alpha", "alpha"));

        Assert.Single(await repo.ListAsync());
    }

    [Fact]
    public async Task RecordChange_appends_commits_and_attaches_sha()
    {
        var repo = new InMemoryProjectRepository();
        var project = Project.Discover(RepoPath.From("/home/brad/code/alpha"), ProjectName.From("alpha"), T0);
        project.Register(T0);
        await repo.AddAsync(project);

        var ledgers = new InMemoryChangeLedgerRepository();
        var uow = new RecordingUnitOfWork();
        var handler = new RecordChangeHandler(repo, ledgers, new FakeGitPort { ShaToReturn = "deadbee" }, uow, new FakeClock(T0));

        var view = await handler.HandleAsync(new RecordChangeCommand(
            project.Id.Value, "claude", IsAgent: true, Rationale: "add feature",
            TouchedFiles: new[] { "a.cs" }, TouchedNodes: new[] { "auth" },
            TestsAdded: 1, TestsRemoved: 0, TestsChanged: 0,
            DebtResolved: 0, DebtIntroduced: 0,
            ArchitectureReviewerRun: true, LiveVerified: true,
            CommitMessage: "feat: add feature"));

        Assert.Equal(1, view.Sequence);
        Assert.Equal("deadbee", view.Commit);
        Assert.Equal("Agent", view.Kind);
        Assert.Equal(1, uow.SaveCount);

        var ledger = await ledgers.GetForProjectAsync(project.Id);
        Assert.Single(ledger.Records);
        Assert.True(ledger.Records[0].IsCommitted);
    }

    [Fact]
    public async Task RebuildGraph_records_tree_snapshot_and_marks_graphified()
    {
        var repo = new InMemoryProjectRepository();
        var project = Project.Discover(RepoPath.From("/home/brad/code/alpha"), ProjectName.From("alpha"), T0);
        project.Register(T0);
        await repo.AddAsync(project);

        var tree = DecisionTree.Of(
            new[]
            {
                new DecisionTreeNode("root", "alpha", TreeNodeKind.Root, 1, false),
                new DecisionTreeNode("file:core.cs", "core.cs", TreeNodeKind.File, 4, true),
            },
            new[] { new DecisionTreeEdge("root", "file:core.cs", ConfidenceTag.Extracted) });
        var provider = new FakeDecisionTreeProvider { Tree = tree };
        var handler = new RebuildGraphHandler(repo, provider, new RecordingUnitOfWork(), new FakeClock(T0));

        var view = await handler.HandleAsync(new RebuildGraphCommand(project.Id.Value));

        Assert.True(view.IsGraphified);
        Assert.Equal(1, view.GodNodeCount); // the one hotspot file
        var reloaded = await repo.GetByIdAsync(project.Id);
        Assert.Equal(ProjectStatus.Graphified, reloaded!.Status);
        Assert.Equal(2, reloaded.LastGraph!.NodeCount);
    }
}
