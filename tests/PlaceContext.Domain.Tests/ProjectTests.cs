using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using Xunit;

namespace PlaceContext.Domain.Tests;

public class ProjectTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 23, 12, 0, 0, TimeSpan.Zero);

    private static Project Discovered() =>
        Project.Discover(ProjectPath.From("/workspace/demo"), ProjectName.From("demo"), T0);

    private static GraphSnapshotRef Snapshot() =>
        GraphSnapshotRef.Of("/workspace/demo/graphify-out/graph.json", T0, 10, 20,
            new[] { GodNode.Of(GraphNodeId.From("auth"), NormLabel.From("Auth"), 12) });

    [Fact]
    public void Discover_starts_in_Discovered_status()
    {
        var p = Discovered();
        Assert.Equal(ProjectStatus.Discovered, p.Status);
        Assert.Null(p.RegisteredAt);
        Assert.False(p.IsGraphified);
    }

    [Fact]
    public void Register_promotes_and_raises_event()
    {
        var p = Discovered();
        p.Register(T0);

        Assert.Equal(ProjectStatus.Registered, p.Status);
        Assert.Equal(T0, p.RegisteredAt);
        Assert.Contains(p.PullDomainEvents(), e => e is Events.ProjectRegistered);
    }

    [Fact]
    public void Register_is_idempotent()
    {
        var p = Discovered();
        p.Register(T0);
        p.PullDomainEvents();
        p.Register(T0.AddMinutes(1));

        Assert.Equal(ProjectStatus.Registered, p.Status);
        Assert.Equal(T0, p.RegisteredAt); // unchanged
    }

    [Fact]
    public void RecordGraphBuild_before_register_throws()
    {
        var p = Discovered();
        Assert.Throws<InvalidOperationException>(() => p.RecordGraphBuild(Snapshot()));
    }

    [Fact]
    public void RecordGraphBuild_after_register_sets_graphified()
    {
        var p = Discovered();
        p.Register(T0);
        p.RecordGraphBuild(Snapshot());

        Assert.Equal(ProjectStatus.Graphified, p.Status);
        Assert.True(p.IsGraphified);
        Assert.Single(p.LastGraph!.GodNodes);
    }

    [Fact]
    public void Archived_project_rejects_further_work()
    {
        var p = Discovered();
        p.Register(T0);
        p.Archive();
        Assert.Throws<InvalidOperationException>(() => p.RecordGraphBuild(Snapshot()));
    }
}
