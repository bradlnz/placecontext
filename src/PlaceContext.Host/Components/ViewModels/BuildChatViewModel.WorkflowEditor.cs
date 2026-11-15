using PlaceContext.Application.Build;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class BuildChatViewModel
{
    public async Task LoadWorkflowEditorAsync(Guid projectId, Guid workflowId, DateTimeOffset version)
    {
        if (Conversation.ProjectId == projectId && Conversation.WorkflowEditorId == workflowId)
        {
            if (!Locked) Conversation.WorkflowEditorVersion = version;
            return;
        }
        StopUpdates();
        Loading = true; Error = Notice = null; Draft = "";
        Conversation = new() { ProjectId = projectId, WorkflowEditorId = workflowId, WorkflowEditorVersion = version, Title = "Workflow editor" };
        try
        {
            Connections = (await connections.ListAsync()).Where(connection => connection.Enabled).ToArray();
            SelectDefaultModel();
            SubscribeToConversation();
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Loading = false; NotifyStateChanged(); }
    }

    public void SelectEditorConnection(string? id)
    {
        if (Locked || !Guid.TryParse(id, out var connectionId)) return;
        Conversation.ConnectionId = connectionId;
        Conversation.Model = Models.FirstOrDefault() ?? "";
        NotifyStateChanged();
    }
}
