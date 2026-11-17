using System.Text.Json.Nodes;
using PlaceContext.Application.Build;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class ContextAssistantViewModel
{
    private BuildConversation? _managementConversation;

    private async Task<AssistantMessage> SendManagementAsync(Guid projectId, string question, CancellationToken ct)
    {
        if (buildAgent is null || buildStore is null) throw new InvalidOperationException("Workflow management is unavailable.");
        if (_managementConversation?.ProjectId != projectId)
            _managementConversation = new() { ProjectId = projectId, WorkflowManagement = true, Title = "Workflow manager" };
        var conversation = _managementConversation;
        conversation.ConnectionId = ConnectionId;
        conversation.Model = Model;
        try
        {
            await buildAgent.QueueAsync(conversation, question, ct);
            BuildConversation latest;
            do
            {
                latest = await buildStore.LoadAsync(projectId, conversation.Id, ct);
                if (latest.Status is not (BuildRunStatus.Queued or BuildRunStatus.Running)) break;
                await Task.Delay(500, ct);
            } while (true);
            ct.ThrowIfCancellationRequested();
            if (latest.Error is not null) throw new InvalidOperationException(latest.Error + " Completed changes remain applied.");
            if (latest.Status != BuildRunStatus.Completed) throw new InvalidOperationException("The management task stopped. Check the workflow list before retrying.");
            var reply = latest.Messages.OfType<JsonNode>().LastOrDefault(message => BuildChatViewModel.Role(message) == "assistant"
                && !string.IsNullOrWhiteSpace(BuildChatViewModel.Text(message)))
                ?? throw new InvalidOperationException("The workflow manager returned no answer.");
            return new("assistant", BuildChatViewModel.Text(reply)) { Answers = BuildChatViewModel.SuggestedAnswers(reply).ToArray() };
        }
        catch (OperationCanceledException)
        {
            await buildStore.RequestStopAsync(projectId, conversation.Id);
            throw;
        }
    }
}
