using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Mcp;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Infrastructure.Caching;
using PlaceContext.Infrastructure.Chat;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class ChatViewModel
{
    // ── Session management ───────────────────────────────────────────────────

    public async Task LoadSessionsAsync()
    {
        if (!ProjectId.HasValue)
            return;
        try
        {
            Sessions = await _memoryStore.ListSessionsAsync(ProjectId.Value);
        }
        catch
        {
            Sessions = Array.Empty<ChatSessionSummary>();
        }
    }

    public void NewSession()
    {
        if (Streaming)
            return;
        _sessionId = Guid.NewGuid();
        _sessionTitle = ChatCopy.DefaultSessionTitle;
        Messages.Clear();
        ActiveActions.Clear();
        FetchedData.Clear();
        ToolHistory.Clear();
        PendingClarification = null;
        ClarificationSelected.Clear();
        RenderedChartIds.Clear();
        AttachedFile = null;
        AttachedFileName = null;
        AttachedFileText = null;
        AttachmentError = null;
        StreamBuffer = "";
        NotifyStateChanged();
    }

    public void OpenCreateChannel()
    {
        if (Streaming)
            return;
        NewChannelName = "";
        ChannelError = null;
        ShowCreateChannel = true;
        NotifyStateChanged();
    }

    public void CancelCreateChannel()
    {
        ShowCreateChannel = false;
        NewChannelName = "";
        ChannelError = null;
        NotifyStateChanged();
    }

    public async Task CreateChannelAsync()
    {
        if (!ProjectId.HasValue || CreatingChannel || Streaming)
            return;

        var name = ChatChannel.NormalizeName(NewChannelName);
        if (string.IsNullOrWhiteSpace(name))
        {
            ChannelError = "Enter a channel name using letters or numbers.";
            NotifyStateChanged();
            return;
        }

        if (Sessions.Any(session =>
                string.Equals(ChatChannel.NormalizeName(session.Title), name, StringComparison.OrdinalIgnoreCase)))
        {
            ChannelError = $"#{name} already exists.";
            NotifyStateChanged();
            return;
        }

        CreatingChannel = true;
        ChannelError = null;
        NotifyStateChanged();
        try
        {
            var channel = ChatChannel.Create(ProjectId.Value, name, DateTimeOffset.Now);
            await _memoryStore.SaveSessionAsync(channel.Id, channel);
            await LoadSessionsAsync();
            var summary = Sessions.First(session => session.Id == channel.Id);
            ShowCreateChannel = false;
            NewChannelName = "";
            await SelectSessionAsync(summary);
        }
        catch (Exception ex)
        {
            ChannelError = $"Could not add the channel: {ex.Message}";
        }
        finally
        {
            CreatingChannel = false;
            NotifyStateChanged();
        }
    }

    public async Task DeleteSessionAsync(ChatSessionSummary session)
    {
        if (Streaming)
            return;
        try
        {
            await _memoryStore.DeleteSessionAsync(session.Id);
        }
        catch { }
        if (session.Id == _sessionId)
            NewSession();
        await LoadSessionsAsync();
        NotifyStateChanged();
    }

    public async Task ClearCurrentSessionAsync()
    {
        if (Streaming)
            return;
        if (_sessionId.HasValue)
        {
            try
            {
                await _memoryStore.ClearSessionMemoryAsync(_sessionId.Value);
            }
            catch { }
        }
        if (!Sessions.Any(session => session.Id == _sessionId))
            _sessionTitle = ChatCopy.DefaultSessionTitle;
        Messages.Clear();
        ActiveActions.Clear();
        FetchedData.Clear();
        ToolHistory.Clear();
        PendingClarification = null;
        ClarificationSelected.Clear();
        RenderedChartIds.Clear();
        AttachedFile = null;
        AttachedFileName = null;
        AttachedFileText = null;
        AttachmentError = null;
        StreamBuffer = "";
        await LoadSessionsAsync();
        NotifyStateChanged();
    }

    public async Task SelectSessionAsync(ChatSessionSummary session)
    {
        if (Streaming || !ProjectId.HasValue)
            return;
        try
        {
            var memory = await _memoryStore.GetSessionAsync(session.Id);
            if (memory != null)
            {
                _sessionId = session.Id;
                _sessionTitle = memory.Title;
                Messages.Clear();
                foreach (var m in memory.Messages)
                {
                    var msg = new AgentMessage(m.Role, m.Content)
                    {
                        Thinking = m.Thinking,
                        AttachmentName = m.AttachmentName,
                        AttachmentKey = m.AttachmentKey,
                        AttachmentContentType = m.AttachmentContentType,
                        AttachmentSizeBytes = m.AttachmentSizeBytes,
                        AttachmentParsed = m.AttachmentParsed,
                        AttachmentExtractedChars = m.AttachmentExtractedChars,
                    };
                    if (m.ToolCalls != null)
                        msg.ToolCalls.AddRange(
                            m.ToolCalls.Select(tc => new ToolCallInfo
                            {
                                ToolName = tc.ToolName,
                                Args = tc.Args,
                                Status = Enum.TryParse<AgentToolCallStatus>(tc.Status, out var s)
                                    ? s
                                    : AgentToolCallStatus.Completed,
                                Result = tc.Result,
                                ResultType = tc.ResultType,
                            })
                        );
                    RecoverLostMapCalls(msg);
                    Messages.Add(msg);
                }
                NotifyStateChanged();
            }
        }
        catch { }
    }

    private async Task SaveCurrentSessionAsync()
    {
        if (!ProjectId.HasValue || _sessionId == null)
            return;
        var memory = new ChatSessionMemory(
            _sessionId.Value,
            ProjectId.Value,
            _sessionTitle,
            Messages
                .Select(m => new ChatMemoryMessage(
                    m.Role,
                    m.Content,
                    DateTimeOffset.Now,
                    m.ToolCalls.Select(tc => new ChatMemoryToolCall(
                            tc.ToolName,
                            tc.Args,
                            tc.Status.ToString(),
                            tc.Result,
                            tc.ResultType
                        ))
                        .ToList(),
                    m.AttachmentName,
                    m.AttachmentKey,
                    m.AttachmentContentType,
                    m.AttachmentSizeBytes,
                    m.Thinking,
                    m.AttachmentParsed,
                    m.AttachmentExtractedChars
                ))
                .ToList(),
            DateTimeOffset.Now,
            DateTimeOffset.Now
        );
        try
        {
            await _memoryStore.SaveSessionAsync(_sessionId.Value, memory);
        }
        catch { }
        await LoadSessionsAsync();
    }
}
