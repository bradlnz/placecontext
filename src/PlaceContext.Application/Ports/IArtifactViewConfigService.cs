namespace PlaceContext.Application.Ports;

public interface IArtifactViewConfigService
{
    ArtifactViewConfig DefaultConfig();
    Task<ArtifactViewConfig> GetAsync(CancellationToken ct = default);
    Task SaveAsync(ArtifactViewConfig config, CancellationToken ct = default);
}
