namespace PlaceContext.Application.Ports;

public interface IServiceConsole
{
    Task<IReadOnlyList<ServiceInstance>> ListAsync(Guid projectId, CancellationToken ct = default);
    Task<Guid> LaunchAsync(Guid projectId, ServiceLaunch service, CancellationToken ct = default);
    Task<ServiceCode?> GetCodeAsync(Guid projectId, Guid serviceId, CancellationToken ct = default);
    Task UpdateCodeAsync(Guid projectId, Guid serviceId, ServiceCode code, CancellationToken ct = default);
    Task ControlAsync(Guid projectId, Guid serviceId, string action, CancellationToken ct = default);
    Task<string> LogsAsync(Guid projectId, Guid serviceId, CancellationToken ct = default);
    Task<Uri> GetSsoEndpointAsync(Guid projectId, Guid serviceId, CancellationToken ct = default);
}
