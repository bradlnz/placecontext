namespace PlaceContext.Application.Ports;

public interface IQemuEnvironmentCatalog
{
    QemuEnvironmentCatalog Get();
    Task<QemuEnvironmentCatalog> GetAsync(Guid projectId, CancellationToken ct = default) => Task.FromResult(Get());
}
