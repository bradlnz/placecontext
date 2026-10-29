using PlaceContext.Application.Build;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class ModelsSettingsViewModel(IModelConnections connections, PortalUiState ui) : PageViewModel
{
    public IReadOnlyList<ModelConnection> Connections { get; private set; } = [];
    public Guid Id { get; private set; }
    public string Name { get; set; } = "";
    public string Protocol { get; set; } = "openai";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Models { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public bool ClearApiKey { get; set; }
    public bool Enabled { get; set; } = true;
    public bool HasApiKey { get; private set; }
    public bool IsLocalCli => Protocol is "codex" or "claude-code";
    public string LoginCommand => Protocol == "codex" ? "codex login" : "claude auth login";
    public bool Editing { get; private set; }
    public bool Busy { get; private set; }
    public string? Error { get; private set; }
    public string? Notice { get; private set; }
    public Guid? Deleting { get; set; }
    public static IReadOnlyList<ProviderPreset> Presets { get; } =
    [
        new("OpenAI", "openai", "https://api.openai.com/v1"),
        new("Anthropic", "anthropic", "https://api.anthropic.com/v1"),
        new("Google Gemini", "openai", "https://generativelanguage.googleapis.com/v1beta/openai"),
        new("OpenRouter", "openai", "https://openrouter.ai/api/v1"),
        new("Ollama", "openai", "http://localhost:11434/v1"),
        new("LM Studio", "openai", "http://localhost:1234/v1"),
        new("Codex", "codex", "cli://codex"),
        new("Claude Code", "claude-code", "cli://claude"),
        new("Custom", "openai", ""),
    ];
    public sealed record ProviderPreset(string Name, string Protocol, string BaseUrl);
    public async Task LoadAsync()
    {
        ui.Set("Settings", "Models");
        try { Connections = await connections.ListAsync(); }
        catch (Exception ex) { Error = ex.Message; }
    }
    public void Add()
    {
        Id = Guid.NewGuid(); Name = "OpenAI"; Protocol = "openai"; BaseUrl = Presets[0].BaseUrl;
        Models = ApiKey = ""; HasApiKey = ClearApiKey = false; Enabled = Editing = true; Error = Notice = null;
    }
    public void Preset(string name)
    {
        var preset = Presets.FirstOrDefault(x => x.Name == name);
        if (preset is null) return;
        Name = preset.Name; Protocol = preset.Protocol; BaseUrl = preset.BaseUrl; Models = "";
        if (IsLocalCli) { ApiKey = ""; ClearApiKey = HasApiKey; }
    }
    public void Edit(ModelConnection connection)
    {
        Id = connection.Id; Name = connection.Name; Protocol = connection.Protocol; BaseUrl = connection.BaseUrl;
        Models = string.Join('\n', connection.Models); ApiKey = ""; Enabled = connection.Enabled;
        HasApiKey = connection.HasApiKey; ClearApiKey = false; Editing = true; Error = Notice = null;
    }
    public void Cancel() { Editing = false; ApiKey = ""; }
    private SaveModelConnection Draft() => new(Id, Name.Trim(), Protocol, BaseUrl.Trim(),
        Models.Split(['\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), Enabled, ApiKey, ClearApiKey);
    public async Task SaveAsync() => await RunAsync(async () =>
    {
        await connections.SaveAsync(Draft()); ApiKey = ""; Editing = false; Notice = "Model connection saved.";
        Connections = await connections.ListAsync();
    });
    public async Task DiscoverAsync() => await RunAsync(async () =>
    {
        await connections.SaveAsync(Draft()); ApiKey = ""; ClearApiKey = false;
        Connections = await connections.ListAsync(); HasApiKey = Connections.Single(x => x.Id == Id).HasApiKey;
        var models = await connections.DiscoverAsync(Id);
        Models = string.Join('\n', models);
        Notice = $"Connection saved. Found {models.Count} models. Keep the models you want available, then save.";
    });
    public async Task DeleteAsync() => await RunAsync(async () =>
    {
        if (Deleting is not { } id) return;
        await connections.DeleteAsync(id); Deleting = null; Editing = false; ApiKey = "";
        Connections = await connections.ListAsync(); Notice = "Connection deleted. Existing conversations are retained.";
    });
    private async Task RunAsync(Func<Task> action)
    {
        if (Busy) return;
        Busy = true; Error = Notice = null;
        try { await action(); } catch (Exception ex) { Error = ex.Message; }
        finally { Busy = false; NotifyStateChanged(); }
    }
}
