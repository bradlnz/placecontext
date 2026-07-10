using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>Shared guard: the project must exist before any data operation touches its store.</summary>
internal static class ProjectDataGuard
{
    public static async Task EnsureExistsAsync(IProjectRepository projects, Guid projectId, CancellationToken ct)
        => _ = await projects.GetByIdAsync(ProjectId.From(projectId), ct)
            ?? throw new InvalidOperationException($"Project {projectId} not found.");
}
