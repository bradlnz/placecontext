namespace PlaceContext.Projects.Integration;

public interface IProjectGraphClient
{
    Task<IReadOnlyList<ProjectGraphHotspot>> GetHotspotsAsync(Guid projectId, CancellationToken ct = default);
}

public sealed record ProjectGraphHotspot(string Label, int Degree);

public interface IProjectEventPublisher
{
    Task RaiseAsync(string eventType, Guid projectId, string payload, CancellationToken ct = default);
}
