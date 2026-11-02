using System.Text.Json.Nodes;
using PlaceContext.Application.Build;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class AgentWorkflowsViewModel
{
    public BuildConversation? Setup { get; private set; }
    public IReadOnlyList<BuildConversationSummary> Setups { get; private set; } = [];
    public string SetupReply { get; set; } = "";
    public bool SetupRunning => Setup?.Status is BuildRunStatus.Queued or BuildRunStatus.Running;
    public JsonNode? SetupResponse => Setup?.Messages.LastOrDefault(x => x?["role"]?.GetValue<string>() == "assistant"
        && x["tool_calls"] is not JsonArray { Count: > 0 } && !string.IsNullOrWhiteSpace(x["content"]?.GetValue<string>()));
    public bool CanReplyToSetup => !Busy && !SetupRunning && Setup is { Messages.Count: > 0 } && SetupReply.Trim().Length is > 0 and <= 4000;
    public static string SetupStatus(BuildRunStatus status) => status switch
    {
        BuildRunStatus.Queued => "Queued", BuildRunStatus.Running => "Preparing agent",
        BuildRunStatus.Completed => "Review setup", BuildRunStatus.Failed => "Setup needs attention",
        BuildRunStatus.Stopped => "Setup paused", BuildRunStatus.Interrupted => "Setup interrupted", _ => "Not started"
    };
    private Task? _setupPoll;

    public Task StartSetupAsync() => !CanGenerateWorkflow || WorkflowNameError is not null ? Task.CompletedTask : ExecuteAsync(async () =>
    {
        if (builder is null || setups is null) throw new InvalidOperationException("Agent setup is unavailable.");
        var template = SelectedVariants.FirstOrDefault(x => x.Paid) ?? throw new InvalidOperationException("Agent not found.");
        Setup ??= new() { ProjectId = ProjectId };
        await builder.QueueAgentSetupAsync(Setup, template.Id, WorkflowName, _stop.Token);
        Setups = (await setups.ListAsync(ProjectId, _stop.Token)).Where(x => x.AgentId is not null).ToArray();
    });

    public Task OpenSetupAsync(Guid id) => ExecuteAsync(async () =>
    {
        if (setups is null) throw new InvalidOperationException("Agent setup is unavailable.");
        var saved = await setups.LoadAsync(ProjectId, id, _stop.Token);
        var template = Workspace?.Workflows.FirstOrDefault(x => x.Id == saved.AgentId && x.Paid)
            ?? throw new InvalidOperationException("Agent setup not found.");
        SelectCore(template);
        Setup = saved; WorkflowName = saved.Title; SetupReply = "";
    });

    public Task ReplyToSetupAsync() => !CanReplyToSetup ? Task.CompletedTask : ExecuteAsync(async () =>
    {
        if (builder is null || Setup is null) throw new InvalidOperationException("Agent setup is unavailable.");
        await builder.QueueAsync(Setup, SetupReply.Trim(), [], _stop.Token);
        SetupReply = "";
    });

    public Task AnswerSetupAsync(string answer)
    {
        if (Busy || SetupRunning || SetupResponse is not { } response || !BuildChatViewModel.SuggestedAnswers(response).Contains(answer)) return Task.CompletedTask;
        SetupReply = answer;
        return ReplyToSetupAsync();
    }

    public Task UseWorkflowInputsAsync()
    {
        if (Busy || SetupRunning || Setup is not { Messages.Count: > 0 }) return Task.CompletedTask;
        SetupReply = "Continue the draft with declared workflow input parameters. Values will be provided at run time "
            + "or mapped from a connected queue or Data table. Do not require sample records to finish setup.";
        return ReplyToSetupAsync();
    }

    public Task StopSetupAsync() => ExecuteAsync(async () =>
    {
        if (setups is not null && Setup is not null && SetupRunning)
            await setups.RequestStopAsync(ProjectId, Setup.Id, _stop.Token);
    });

    public Task RefreshSetupAsync() => ExecuteAsync(async () =>
    {
        if (setups is not null && Setup is { Messages.Count: > 0 })
        {
            Setup = await setups.LoadAsync(ProjectId, Setup.Id, _stop.Token);
            if (!SetupRunning) Setups = (await setups.ListAsync(ProjectId, _stop.Token)).Where(x => x.AgentId is not null).ToArray();
        }
    });

    private async Task PollSetupAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            while (await timer.WaitForNextTickAsync(ct))
                if (SetupRunning) await RefreshSetupAsync();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
}
