using Microsoft.JSInterop;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class BuildChatViewModel
{
    private int _mentionStart = -1;
    private int _mentionEnd;
    private string? _mentionDraft;
    private int? _composerCaret;
    public string McpMentionQuery { get; private set; } = "";
    public IReadOnlyList<McpConnectionView> MatchingMcps => AvailableMcps
        .Where(server => server.Name.Contains(McpMentionQuery, StringComparison.OrdinalIgnoreCase)).ToArray();

    [JSInvokable]
    public async Task UpdateMcpMention(string draft, int start, int end)
    {
        if (Locked || Listening || draft.Length > 16000)
        {
            return;
        }
        Draft = draft;
        if (start < 0 || end <= start || end > draft.Length || draft[start] != '@')
        {
            if (_mentionStart < 0)
            {
                return;
            }
            ResetMcpMention();
            ShowMcpMenu = false;
            if (_module is not null)
            {
                await _module.InvokeVoidAsync("setComposerMenu", ViewId, false);
            }
            NotifyStateChanged();
            return;
        }
        var opening = _mentionStart < 0;
        _mentionStart = start;
        _mentionEnd = end;
        _mentionDraft = draft;
        McpMentionQuery = draft[(start + 1)..end].TrimStart('"');
        ShowMcpMenu = true;
        if (opening)
        {
            await LoadMcpMenuAsync(false);
        }
        if (_mentionStart < 0 || _mentionDraft != Draft || !ShowMcpMenu)
        {
            return;
        }
        NotifyStateChanged();
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("setComposerMenu", ViewId, true);
        }
    }

    private void ResetMcpMention()
    {
        _mentionStart = -1;
        _mentionEnd = 0;
        _mentionDraft = null;
        McpMentionQuery = "";
    }

    public async Task RestoreComposerCaretAsync()
    {
        if (_composerCaret is { } caret && _module is not null)
        {
            _composerCaret = null;
            await _module.InvokeVoidAsync("focusComposer", ViewId, caret);
        }
    }
}
