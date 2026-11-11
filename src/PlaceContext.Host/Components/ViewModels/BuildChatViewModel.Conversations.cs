using System.Text.Json.Nodes;
using Microsoft.JSInterop;
using PlaceContext.Application.Build;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class BuildChatViewModel
{
    public string HistoryQuery { get; set; } = "";
    public IEnumerable<BuildConversationSummary> FilteredConversations => Conversations
        .Where(chat => chat.Title.Contains(HistoryQuery.Trim(), StringComparison.OrdinalIgnoreCase));

    private PendingBranch? _branch;
    private sealed record PendingBranch(Guid SourceId, int MessageCount, JsonArray Prefix, string Label);
    public bool BranchPending => _branch is not null;
    public string? BranchLabel => _branch?.Label;
    private bool CanChooseBranch => !Locked && !Listening && !BranchPending && string.IsNullOrWhiteSpace(Draft);

    public bool CanEditMessage(JsonNode message) => CanChooseBranch
        && Conversation.Messages.Contains(message) && Role(message) == "user"
        && !ImageUrls(message).Any() && Text(message).Length is > 0 and <= 16000;

    public bool CanBranchMessage(JsonNode message) => CanChooseBranch
        && Conversation.Messages.Contains(message) && Role(message) == "assistant"
        && !Calls(message).Any() && Text(message).Length > 0;

    private JsonNode? UserMessageBefore(JsonNode message) => Conversation.Messages
        .Take(Conversation.Messages.IndexOf(message)).LastOrDefault(node => node is not null && Role(node) == "user");

    public bool CanRetryMessage(JsonNode message) => CanBranchMessage(message)
        && UserMessageBefore(message) is { } prompt && CanEditMessage(prompt);

    public void EditMessage(JsonNode message)
    {
        if (!CanEditMessage(message))
        {
            return;
        }
        PrepareBranch(Conversation.Messages.IndexOf(message), "Editing in a new conversation", Text(message));
    }

    public void RetryMessage(JsonNode message)
    {
        if (!CanRetryMessage(message) || UserMessageBefore(message) is not { } prompt)
        {
            return;
        }
        PrepareBranch(Conversation.Messages.IndexOf(prompt), "Retrying in a new conversation", Text(prompt));
    }

    public void BranchFromMessage(JsonNode message)
    {
        if (!CanBranchMessage(message))
        {
            return;
        }
        PrepareBranch(Conversation.Messages.IndexOf(message) + 1, "Continuing in a new conversation", "");
    }

    private void PrepareBranch(int messageCount, string label, string draft)
    {
        _branch = new(Conversation.Id, messageCount,
            new JsonArray(Conversation.Messages.Take(messageCount).Select(node => node?.DeepClone()).ToArray()), label);
        Draft = draft;
        Error = Notice = null;
    }

    public void CancelBranch()
    {
        if (Submitting)
        {
            return;
        }
        _branch = null;
        Draft = "";
    }

    private async Task<BuildConversation> PrepareSendConversationAsync()
    {
        if (_branch is not { } branch)
        {
            return Conversation;
        }
        // Re-read through the owned store before copying history. A stale UI must not
        // fork a deleted, inaccessible, changed or currently running conversation.
        var source = await store.LoadAsync(Conversation.ProjectId, branch.SourceId);
        var prefix = new JsonArray(source.Messages.Take(branch.MessageCount).Select(node => node?.DeepClone()).ToArray());
        if (source.Status is BuildRunStatus.Queued or BuildRunStatus.Running
            || source.Messages.Count < branch.MessageCount || !JsonNode.DeepEquals(prefix, branch.Prefix))
        {
            throw new InvalidOperationException("The original conversation changed. Cancel this draft and open it again before branching.");
        }
        var title = "Branch: " + source.Title;
        return new()
        {
            ProjectId = source.ProjectId,
            AgentId = Conversation.AgentId,
            ConnectionId = Conversation.ConnectionId,
            Model = Conversation.Model,
            Title = title[..Math.Min(80, title.Length)],
            Messages = prefix,
            ParentConversationId = source.Id,
            ParentMessageCount = branch.MessageCount
        };
    }

    public async Task CopyMessageAsync(JsonNode message)
    {
        if (!Conversation.Messages.Contains(message) || string.IsNullOrEmpty(Text(message)))
        {
            return;
        }
        try
        {
            await js.InvokeVoidAsync("navigator.clipboard.writeText", Text(message));
            Notice = "Message copied.";
        }
        catch (JSException)
        {
            Notice = "Clipboard access is unavailable. Select the message text to copy it.";
        }
    }
}
