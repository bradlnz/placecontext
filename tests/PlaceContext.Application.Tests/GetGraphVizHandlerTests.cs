using PlaceContext.Application.Features;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Tests;

public sealed class GetGraphVizHandlerTests
{
    [Fact]
    public async Task Artifact_nodes_include_metadata_required_for_inline_preview()
    {
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 8, 18, 1, 2, 3, TimeSpan.Zero);
        var artifact = RunArtifactLink.Create(
            runId,
            jobId,
            projectId,
            PostJobActionKind.RawBundle,
            "site-plan.pdf",
            "artifacts",
            "runs/site-plan.pdf",
            "application/pdf",
            1234,
            createdAt
        );
        var tree = DecisionTree.Of(
            [new DecisionTreeNode($"artifact:{artifact.Id:N}", artifact.Title, TreeNodeKind.Artifact, 0, false)],
            []
        );
        var handler = new GetGraphVizHandler(
            new StubTreeProvider(tree),
            new StubArtifactRepository([artifact])
        );

        var graph = await handler.HandleAsync(new GetGraphVizQuery(projectId));

        var reference = Assert.Single(graph.Nodes).Artifact;
        Assert.NotNull(reference);
        Assert.Equal(artifact.Id, reference.Id);
        Assert.Equal(runId, reference.RunId);
        Assert.Equal("site-plan.pdf", reference.Title);
        Assert.Equal("application/pdf", reference.ContentType);
        Assert.Equal(createdAt, reference.CreatedAt);
    }

    private sealed class StubTreeProvider(DecisionTree tree) : IDecisionTreeProvider
    {
        public Task<DecisionTree> BuildAsync(
            ProjectId projectId,
            CancellationToken ct = default
        ) => Task.FromResult(tree);
    }

    private sealed class StubArtifactRepository(IReadOnlyList<RunArtifactLink> artifacts)
        : IRunArtifactLinkRepository
    {
        public Task<IReadOnlyList<RunArtifactLink>> ListForProjectAsync(
            Guid projectId,
            int take,
            string? search = null,
            CancellationToken ct = default
        ) => Task.FromResult(artifacts);

        public Task AddAsync(RunArtifactLink link, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<RunArtifactLink>> ListForRunAsync(
            Guid runId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<RunArtifactLink>>([]);

        public Task<RunArtifactLink?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<RunArtifactLink?>(null);

        public Task<IReadOnlyList<RunArtifactLink>> ListForJobAsync(
            Guid jobId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<RunArtifactLink>>([]);

        public Task<IReadOnlyList<RunArtifactLink>> ListRecentAsync(
            int take,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<RunArtifactLink>>([]);

        public Task<IReadOnlyList<RunArtifactLink>> ListPendingOcrAsync(
            int take,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<RunArtifactLink>>([]);

        public Task MarkOcrProcessedAsync(
            Guid artifactId,
            DateTimeOffset processedAt,
            string? error,
            CancellationToken ct = default
        ) => Task.CompletedTask;

        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }
}
