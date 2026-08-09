using System.Text.Json;

namespace PlaceContext.Communications;

public interface ICommunicationDirectoryClient
{
    Task<JsonElement> ListProjectsAsync(CancellationToken ct = default);
    Task<JsonElement> ListSecretsAsync(Guid projectId, CancellationToken ct = default);
}
