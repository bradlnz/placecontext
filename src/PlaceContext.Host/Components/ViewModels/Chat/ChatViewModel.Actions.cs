using PlaceContext.Application;
using PlaceContext.Application.Agents;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Mcp;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.AgentChat.Infrastructure.Caching;
using PlaceContext.AgentChat.Infrastructure.Chat;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class ChatViewModel
{
    // ── Quick actions ────────────────────────────────────────────────────────

    public async Task QuickActionAsync(string context, string action, Func<Task> scrollToBottom)
    {
        if (Streaming || !ProjectId.HasValue)
            return;
        Input = action switch
        {
            "summarize" => "Summarize the following in 3 bullet points:\n\n" + context,
            "explain" => "Explain the following in more detail, breaking down each concept:\n\n"
                + context,
            "code" => "Generate clean, well-commented code based on the following description:\n\n"
                + context,
            "graph" => "Create a visual graph/chart description based on the following:\n\n"
                + context,
            _ => context,
        };
        await SendAsync(scrollToBottom, scrollToBottom);
    }

    public async Task StarterPromptAsync(string prompt, Func<Task> scrollToBottom)
    {
        if (Streaming || !ProjectId.HasValue)
            return;
        Input = prompt;
        await SendAsync(scrollToBottom, scrollToBottom);
    }

    // ── Clarification ────────────────────────────────────────────────────────

    public void ToggleClarificationOption(string id)
    {
        if (ClarificationSelected.Contains(id))
            ClarificationSelected.Remove(id);
        else
            ClarificationSelected.Add(id);
        NotifyStateChanged();
    }

    public void CancelClarification()
    {
        PendingClarification = null;
        ClarificationSelected.Clear();
        _clarificationTcs?.TrySetResult(new ClarificationResult { Confirmed = false });
        _clarificationTcs = null;
        NotifyStateChanged();
    }

    public void SubmitClarification()
    {
        if (PendingClarification == null)
            return;
        var selectedLabels = PendingClarification
            .Options.Where(o => ClarificationSelected.Contains(o.Id))
            .Select(o => o.Label)
            .ToList();
        var userResponse =
            selectedLabels.Count == 1
                ? $"Selected: {selectedLabels[0]}"
                : $"Selected: {string.Join(", ", selectedLabels)}";
        Messages.Add(new AgentMessage("user", userResponse));
        var result = new ClarificationResult
        {
            Confirmed = true,
            SelectedIds = ClarificationSelected.ToList(),
        };
        PendingClarification = null;
        ClarificationSelected.Clear();
        _clarificationTcs?.TrySetResult(result);
        _clarificationTcs = null;
        NotifyStateChanged();
    }

    public async Task<ClarificationResult> AskClarificationAsync(ClarificationRequest request)
    {
        ClarificationSelected.Clear();
        PendingClarification = request;
        _clarificationTcs = new TaskCompletionSource<ClarificationResult>();
        NotifyStateChanged();
        return await _clarificationTcs.Task;
    }

    // ── Active actions / fetched data / tool history ─────────────────────────

    public void AddActiveAction(string toolName, string detail) =>
        ActiveActions.Add(
            new AgentAction
            {
                ToolName = toolName,
                Detail = detail,
                Status = AgentToolCallStatus.Running,
            }
        );

    public void CompleteActiveAction(string toolName, bool success)
    {
        var action = ActiveActions.FirstOrDefault(a =>
            a.ToolName == toolName && a.Status == AgentToolCallStatus.Running
        );
        if (action != null)
            action.Status = success ? AgentToolCallStatus.Completed : AgentToolCallStatus.Error;
    }

    public void AddFetchedData(string source, int rowCount, string preview) =>
        FetchedData.Add(
            new FetchedData
            {
                Source = source,
                RowCount = rowCount,
                Preview = preview,
            }
        );

    public void AddToolHistory(string toolName, bool success, string status) =>
        ToolHistory.Add(
            new ToolHistoryEntry
            {
                ToolName = toolName,
                Success = success,
                Status = status,
                Timestamp = DateTimeOffset.Now,
            }
        );

    // ── Recover lost map calls from old sessions ─────────────────────────────

    private static void RecoverLostMapCalls(AgentMessage msg)
    {
        if (msg.Role != "assistant" || string.IsNullOrEmpty(msg.Content))
            return;
        if (
            !msg.Content.Contains(
                $"{AgentToolNames.ToolCallPrefix}{AgentToolNames.RenderMap}",
                StringComparison.Ordinal
            )
        )
            return;
        foreach (
            var call in ScanToolCalls(msg.Content).Where(c => c.Name == AgentToolNames.RenderMap)
        )
        {
            if (msg.ToolCalls.Any(t => t.ResultType == "map" && t.Args == call.Args))
                continue;
            try
            {
                System.Text.Json.JsonDocument.Parse(call.Args);
            }
            catch
            {
                continue;
            }
            msg.ToolCalls.Add(
                new ToolCallInfo
                {
                    ToolName = AgentToolNames.RenderMap,
                    Args = call.Args,
                    Status = AgentToolCallStatus.Completed,
                    Result = call.Args,
                    ResultType = "map",
                }
            );
        }
    }
}
