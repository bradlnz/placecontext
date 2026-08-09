namespace PlaceContext.Projects.Integration;

public interface IProjectEventPublisher
{
    Task RaiseAsync(string eventType, Guid projectId, string payload, CancellationToken ct = default);
}
