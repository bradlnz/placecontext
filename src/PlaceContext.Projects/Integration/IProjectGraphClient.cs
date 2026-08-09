namespace PlaceContext.Projects.Integration;

public interface IProjectGraphClient
{
    Task<IReadOnlyList<ProjectGraphHotspot>> GetHotspotsAsync(Guid projectId, CancellationToken ct = default);
}
