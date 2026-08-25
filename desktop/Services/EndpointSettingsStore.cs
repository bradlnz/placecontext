using System.Text.Json;

namespace PlaceContext.Desktop.Services;

public sealed class EndpointSettingsStore
{
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PlaceContext",
        "desktop.json");

    public async Task<SavedEndpointSettings?> LoadAsync()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return null;
            await using var stream = File.OpenRead(_settingsPath);
            return await JsonSerializer.DeserializeAsync<SavedEndpointSettings>(stream);
        }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
    }

    public async Task SaveAsync(string endpoint)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        await using var stream = File.Create(_settingsPath);
        await JsonSerializer.SerializeAsync(stream, new SavedEndpointSettings(endpoint));
    }
}

public sealed record SavedEndpointSettings(string Endpoint);
