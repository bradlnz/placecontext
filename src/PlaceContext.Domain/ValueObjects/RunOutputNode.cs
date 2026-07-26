namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// Input to <see cref="Services.DecisionTreeAssembler"/> representing one job-run output that has been
/// embedded (organized output + its vector). The assembler weaves these into the knowledge graph as
/// <see cref="TreeNodeKind.JobRunOutput"/> nodes and cross-links the semantically-nearest ones, so the
/// accumulated run outputs become the project's connective "brain" — queryable memory wired into the graph.
/// </summary>
public sealed record RunOutputNode(string Id, string Label, IReadOnlyList<float> Vector,
    /// <summary>The job that produced this output — when known, the node links to its job instead of the root.</summary>
    Guid? JobId = null);
