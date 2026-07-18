namespace PlaceContext.Application.Features;

/// <summary>All occurrences of one value: the group a normalized value forms across a project's tables.</summary>
public sealed record RecordLinkGroup(string Kind, string NormalizedValue, string DisplayValue,
    IReadOnlyList<RecordLink> Occurrences);

/// <summary>Persistence port for the record-link index (a link store, not an aggregate — like entity tags).</summary>
public interface IRecordLinkStore
{
    /// <summary>Replaces the project's whole index with the fresh scan's links.</summary>
    Task ReplaceForProjectAsync(Guid projectId, IReadOnlyList<RecordLink> links, CancellationToken ct = default);

    /// <summary>Replaces one table's slice of the index (run after any write to that table).</summary>
    Task ReplaceForTableAsync(Guid projectId, string table, IReadOnlyList<RecordLink> links, CancellationToken ct = default);

    /// <summary>Occurrences sharing a normalized value with this row's links, in OTHER tables/rows.</summary>
    Task<IReadOnlyList<RecordLink>> RelatedAsync(Guid projectId, string table, string rowKey, int take = 20, CancellationToken ct = default);

    /// <summary>Values occurring at least twice anywhere in the project, largest groups first.</summary>
    Task<IReadOnlyList<RecordLinkGroup>> GroupsAsync(Guid projectId, int take = 50, CancellationToken ct = default);
}
