using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.AgentChat.Infrastructure.Caching;
using PlaceContext.AgentChat.Infrastructure.Chat;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class ChatViewModel
{
    public IReadOnlyList<ChatCommandView> Commands { get; private set; } =
        Array.Empty<ChatCommandView>();
    public bool ShowAddCommand { get; set; }
    public string NewCmdName { get; set; } = "";
    public string NewCmdDescription { get; set; } = "";
    public string NewCmdToolName { get; set; } = "";
    public string NewCmdArgs { get; set; } = "";

    public async Task LoadCommandsAsync()
    {
        if (!ProjectId.HasValue)
            return;
        try
        {
            Commands = await _svc.ListChatCommandsAsync(ProjectId.Value);
        }
        catch
        {
            Commands = Array.Empty<ChatCommandView>();
        }
        NotifyStateChanged();
    }

    public void ShowAddCommandForm()
    {
        NewCmdName = "";
        NewCmdDescription = "";
        NewCmdToolName = "";
        NewCmdArgs = "";
        ShowAddCommand = true;
        NotifyStateChanged();
    }

    public async Task AddCommandAsync()
    {
        if (
            !ProjectId.HasValue
            || string.IsNullOrWhiteSpace(NewCmdName)
            || string.IsNullOrWhiteSpace(NewCmdToolName)
        )
            return;
        try
        {
            await _svc.CreateChatCommandAsync(
                new CreateChatCommandCommand(
                    ProjectId.Value,
                    NewCmdName.TrimStart('/'),
                    NewCmdDescription,
                    NewCmdToolName,
                    NewCmdArgs
                )
            );
            ShowAddCommand = false;
            await LoadCommandsAsync();
        }
        catch { }
    }

    public async Task DeleteCommandAsync(Guid id)
    {
        try
        {
            await _svc.DeleteChatCommandAsync(id);
            await LoadCommandsAsync();
        }
        catch { }
    }
}
