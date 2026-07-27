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
            _temperature = config.Temperature;
            _maxContextChunks = config.MaxContextChunks;
        }
        catch { }
    }

    public void OpenSettings()
    {
        PendingSystemPrompt = _systemPrompt;
        PendingTemperature = _temperature;
        PendingMaxTokens = _maxTokens;
        PendingRagEnabled = _ragEnabled;
        PendingMaxContextChunks = _maxContextChunks;
        SettingsTab = "prompt";
        ShowAddMcp = false;
        ShowSettings = true;
        NotifyStateChanged();
    }

    public void CloseSettings() { ShowSettings = false; NotifyStateChanged(); }

    public async Task SaveSettingsAsync()
    {
        _systemPrompt = PendingSystemPrompt;
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
                    config.ProjectId, config.BaseModel, _systemPrompt, _maxContextChunks,
                    _temperature, config.TopP, config.Enabled));
            }
            catch { }
        }
        NotifyStateChanged();
    }

}
