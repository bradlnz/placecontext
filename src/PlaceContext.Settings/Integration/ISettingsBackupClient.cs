using System.Text.Json;

namespace PlaceContext.Settings.Integration;

public interface ISettingsBackupClient
{
    Task<JsonElement> ImportAsync(JsonElement manifest, CancellationToken ct = default);
}
