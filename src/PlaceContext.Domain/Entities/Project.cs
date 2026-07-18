using PlaceContext.Domain.Common;
using PlaceContext.Domain.Events;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Entities;

/// <summary>
/// Aggregate Root for a project under PlaceContext's watch. The unit of persistence and the only
/// entry point to its registry record. No public setters — state changes go through methods that
/// protect the lifecycle (<see cref="ProjectStatus"/>). Construction is via <see cref="Discover"/>
/// (a freshly-scanned candidate) or <see cref="Rehydrate"/> (loading from the store).
/// </summary>
public sealed class Project : AggregateRoot
{
    private Project(
        ProjectId id,
        ProjectName name,
        ProjectPath path,
        ProjectStatus status,
        DateTimeOffset discoveredAt,
        DateTimeOffset? registeredAt,
        GraphSnapshotRef? lastGraph)
    {
        Id = id;
        Name = name;
        Path = path;
        Status = status;
        DiscoveredAt = discoveredAt;
        RegisteredAt = registeredAt;
        LastGraph = lastGraph;
    }

    public ProjectId Id { get; }
    public ProjectName Name { get; private set; }
    public ProjectPath Path { get; }
    public ProjectStatus Status { get; private set; }
    public DateTimeOffset DiscoveredAt { get; }
    public DateTimeOffset? RegisteredAt { get; private set; }
    public GraphSnapshotRef? LastGraph { get; private set; }

    public bool IsGraphified => LastGraph is not null;

    /// <summary>Factory for a freshly-discovered candidate (not yet registered).</summary>
    public static Project Discover(ProjectPath path, ProjectName name, DateTimeOffset discoveredAt)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(name);

        return new Project(
            ProjectId.New(), name, path, ProjectStatus.Discovered,
            discoveredAt, registeredAt: null, lastGraph: null);
    }

    /// <summary>Rebuilds an aggregate from persisted state. Used only by the Infrastructure repository.</summary>
    public static Project Rehydrate(
        ProjectId id,
        ProjectName name,
        ProjectPath path,
        ProjectStatus status,
        DateTimeOffset discoveredAt,
        DateTimeOffset? registeredAt,
        GraphSnapshotRef? lastGraph)
        => new(id, name, path, status, discoveredAt, registeredAt, lastGraph);

    /// <summary>Promotes a discovered candidate to a registered, watched project.</summary>
    public void Register(DateTimeOffset registeredAt)
    {
        if (Status == ProjectStatus.Archived)
            throw new InvalidOperationException("Cannot register an archived project.");
        if (Status != ProjectStatus.Discovered)
            return; // idempotent: already registered/graphified

        Status = ProjectStatus.Registered;
        RegisteredAt = registeredAt;
        Raise(new ProjectRegistered(Id, Name, registeredAt));
    }

    /// <summary>Renames the project (e.g. the user overrides the directory-derived default).</summary>
    public void Rename(ProjectName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
    }

    /// <summary>Records a completed graphify build. Requires the project to be registered first.</summary>
    public void RecordGraphBuild(GraphSnapshotRef snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (Status is ProjectStatus.Discovered)
            throw new InvalidOperationException("Register the project before building its graph.");
        if (Status == ProjectStatus.Archived)
            throw new InvalidOperationException("Cannot build the graph of an archived project.");

        LastGraph = snapshot;
        Status = ProjectStatus.Graphified;
        Raise(new GraphRebuilt(Id, snapshot, snapshot.BuiltAt));
    }

    /// <summary>Stops tracking the project. Terminal state.</summary>
    public void Archive() => Status = ProjectStatus.Archived;
}
