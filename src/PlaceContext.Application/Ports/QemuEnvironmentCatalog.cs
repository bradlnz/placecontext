namespace PlaceContext.Application.Ports;
public sealed record QemuEnvironmentCatalog(bool Enabled, IReadOnlyList<QemuImageProfile> Images, bool AgentReady = false, string? AgentModel = null);
