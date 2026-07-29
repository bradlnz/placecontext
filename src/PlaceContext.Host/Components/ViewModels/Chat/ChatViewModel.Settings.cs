using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Mcp;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Infrastructure.Chat;
using PlaceContext.Infrastructure.Caching;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class ChatViewModel
{
    // ── Agent config ─────────────────────────────────────────────────────────

    private async Task LoadAgentConfigAsync()
    {
        if (!ProjectId.HasValue) return;
        try
        {
            var config = await _svc.GetAgentConfigAsync(ProjectId.Value);
            _systemPrompt = config.SystemPrompt;
            _preamble = string.IsNullOrWhiteSpace(config.Preamble) ? ChatCopy.DefaultPreamble : config.Preamble;
            _toolCatalog = string.IsNullOrWhiteSpace(config.ToolCatalog) ? ChatCopy.DefaultToolCatalog : config.ToolCatalog;
            _launchpadToolCatalog = string.IsNullOrWhiteSpace(config.LaunchpadToolCatalog) ? ChatCopy.DefaultLaunchpadToolCatalog : config.LaunchpadToolCatalog;
            _temperature = config.Temperature;
            _maxContextChunks = config.MaxContextChunks;
        }
        catch { }
    }

    public async Task OpenSettingsAsync()
    {
        PendingSystemPrompt = _systemPrompt;
        PendingPreamble = _preamble;
        PendingToolCatalog = _toolCatalog;
        PendingLaunchpadToolCatalog = _launchpadToolCatalog;
        PendingTemperature = _temperature;
        PendingMaxTokens = _maxTokens;
        PendingRagEnabled = _ragEnabled;
        PendingMaxContextChunks = _maxContextChunks;
        SettingsTab = "prompt";
        ShowAddMcp = false;
        ShowSettings = true;
        NotifyStateChanged();
        await LoadMcpConnectionsAsync();
    }

    public void CloseSettings() { ShowSettings = false; NotifyStateChanged(); }

    public async Task SaveSettingsAsync()
    {
        _systemPrompt = PendingSystemPrompt;
        _preamble = PendingPreamble;
        _toolCatalog = PendingToolCatalog;
        _launchpadToolCatalog = PendingLaunchpadToolCatalog;
        _temperature = PendingTemperature;
        _maxTokens = PendingMaxTokens;
        _ragEnabled = PendingRagEnabled;
        _maxContextChunks = PendingMaxContextChunks;
        ShowSettings = false;
        if (ProjectId.HasValue)
        {
            try
            {
                var config = await _svc.GetAgentConfigAsync(ProjectId.Value);
                await _svc.UpdateAgentConfigAsync(new UpdateAgentConfigCommand(
                    config.ProjectId, config.BaseModel, _systemPrompt, _preamble, _toolCatalog,
                    _launchpadToolCatalog, _maxContextChunks,
                    _temperature, config.TopP, config.Enabled));
            }
            catch { }
        }
        NotifyStateChanged();
    }

}
