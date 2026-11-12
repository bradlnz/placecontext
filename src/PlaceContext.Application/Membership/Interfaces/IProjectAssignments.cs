namespace PlaceContext.Application.Membership;

public interface IProjectAssignments
{
    // Null means all projects; an empty list means no projects.
    Task<IReadOnlyList<Guid>?> GetAsync(Guid userId, CancellationToken ct = default);
    Task SaveAsync(Guid userId, IReadOnlyList<Guid>? projectIds, CancellationToken ct = default);
}
