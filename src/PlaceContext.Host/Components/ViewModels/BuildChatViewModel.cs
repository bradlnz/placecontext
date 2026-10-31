using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using PlaceContext.Application.Build;
using PlaceContext.Host.Build;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class BuildChatViewModel(IBuildChatStore store, IModelConnections connections, BuildChatAgent agent,
    PortalUiState ui, IJSRuntime js) : PageViewModel, IComponentViewModel
{
    public string ViewId { get; } = "build-" + Guid.NewGuid().ToString("N");
    public BuildConversation Conversation { get; private set; } = new();
    public IReadOnlyList<BuildConversationSummary> Conversations { get; private set; } = [];
    public IReadOnlyList<ModelConnection> Connections { get; private set; } = [];
    public IEnumerable<string> Models => Connections.FirstOrDefault(x => x.Id == Conversation.ConnectionId)?.Models ?? [];
    public string? ModelSetupHint => Connections.FirstOrDefault(x => x.Id == Conversation.ConnectionId)?.SetupHint;
    public List<BuildImage> Images { get; } = [];
    public string Draft { get; set; } = "";
    public static IReadOnlyList<(string Title, string Description, string Icon, string Prompt)> QuickPicks { get; } =
    [
        ("Understand phone operations", "Investigate queues, routing and service costs.", "chain",
            "Help me build an enterprise phone operations agent. First inspect the project's available data and workflows, then identify which questions the evidence can support: abandoned calls, number ownership, routing, call quality or service costs. Ask only for missing essential context. Explain source coverage and uncertainty, propose an evidence-linked review workflow, and ask before changing any phone service or contacting anyone."),
        ("Understand my data", "Explore profiles, confidence and what needs attention.", "data.graph",
            "Help me understand the data already available in this project. Inspect sources and existing charts, identify useful profiles, and explain freshness, coverage and missing evidence. Suggest questions supported by the data and the next investigation for weak evidence. Distinguish observed facts, assumptions and model estimates. Ask before creating or changing a workflow."),
        ("Compare grocery prices", "Compare Coles and Woolworths for your postcode.", "vm",
            "Build a grocery price comparison workflow for Coles and Woolworths in Australia. Ask me for the product name and confirm the postcode (default 4101). Inspect existing jobs and workflows first and reuse what fits. Use QEMU Live preview frames and Jev to verify prices, pack sizes and availability. Compare matching products by unit price, link the evidence, and mark any unverified price clearly."),
        ("Check a website", "Give a browser a task and verify what happened.", "check",
            "Help me build a QEMU browser workflow to check a real website. Ask for the starting URL, the task and the expected result. Inspect existing workflows first. Use Live preview to guide the browser and Jev to verify the outcome, with replay evidence and a clear pass or fail result."),
        ("Create a report", "Turn your data into a summary and useful charts.", "data.graph",
            "Help me build a report with a concise summary and charts. First inspect the project's existing jobs and data sources, then ask which data and questions the report should cover. Reuse existing work where possible, use real data, and make missing data clear. Ask whether this should run once or on a schedule."),
        ("Add a review step", "Check a result with Jev or ask a person to approve it.", "agent",
            "Help me add a review step to a workflow. Inspect the existing workflows and ask which one to use, what needs checking, and whether Jev or a person should review it. Define the approval criteria and an explicit path for approved, rejected or uncertain results. Preserve the other workflow steps."),
        ("Onboard a client", "Collect information and check what is still missing.", "human",
            "Help me build a lead qualification and onboarding workflow for my industry. Inspect existing data sources and workflows, then collect only essential missing inputs. Measure lead completeness against explicit criteria. Use Jev to assess whether the evidence supports onboarding. Route low support, ambiguous judgments or incomplete evidence to a durable Human in the loop Yes/No approval step; show the human the underlying evidence. Configure a safe No branch that does not contact or onboard the lead. Continue only after the appropriate review and explicit approval. Ask whether to test once or activate a schedule."),
        ("Fix a failed workflow", "Trace a failed run and find the smallest fix.", "logs",
            "Help me diagnose a failed workflow. Inspect the available workflows and ask which run failed if it is unclear. Read its execution results and relevant definitions, explain the cause using the evidence, and make the smallest fix. Preserve unrelated steps and verify the change.")
    ];
    public bool CanUseQuickPick => !Locked && !Listening && string.IsNullOrWhiteSpace(Draft);
    public bool UseQuickPick(string prompt)
    {
        if (!CanUseQuickPick) return false;
        Draft = prompt;
        return true;
    }
    public async Task<bool> UseAgentRequestAsync(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return false;
        if (prompt.Length > 4000)
        {
            Notice = "This agent request is too long. Choose the agent again from Agent Store.";
            return false;
        }
        if (NavigationLocked || Listening) return false;
        if (!string.IsNullOrWhiteSpace(Draft) || Images.Count > 0)
        {
            Notice = "Your current draft is still here. Send or clear it before bringing an agent request into Build.";
            return false;
        }
        if (Conversation.Messages.Count > 0 || Busy) await NewAsync();
        if (!UseQuickPick(prompt.Trim())) return false;
        Notice = "Agent request ready. Review it, then send when you are ready to build.";
        return true;
    }
    public string Memory { get; set; } = "";
    public string? Error { get; private set; }
    public string? Notice { get; private set; }
    public bool Busy => Conversation.Status is BuildRunStatus.Queued or BuildRunStatus.Running;
    public bool Submitting { get; private set; }
    public bool NavigationLocked => Loading || Uploading || Submitting;
    public bool Loading { get; private set; }
    public bool Uploading { get; private set; }
    public bool Locked => Busy || NavigationLocked;
    public bool ShowMemory { get; set; }
    public bool ShowHistory { get; set; }
    public bool ConfirmDelete { get; set; }
    public bool SpeechSupported { get; private set; }
    public bool Listening { get; private set; }
    public bool CanSend => !Locked && !Listening && Conversation.ConnectionId != Guid.Empty
        && Models.Contains(Conversation.Model) && (Draft.Trim().Length > 0 || Images.Count > 0);
    private CancellationTokenSource? _pollCancel;
    private Task? _poll;
    private Guid? _requestedConversationId;
    private Task? _send;
    private IJSObjectReference? _module;
    private DotNetObjectReference<BuildChatViewModel>? _reference;

    public async Task LoadAsync(Guid projectId, Guid? conversationId = null)
    {
        if (Conversation.ProjectId == projectId)
        {
            if (_requestedConversationId != conversationId && conversationId is { } id) await OpenAsync(id);
            _requestedConversationId = conversationId;
            return;
        }
        _requestedConversationId = conversationId;
        await StopPollingAsync();
        if (_send is not null) await _send;
        await StopSpeechAsync();
        Loading = true; Error = Notice = null; Draft = ""; Images.Clear();
        Conversation = new() { ProjectId = projectId };
        try
        {
            ui.Set("Build", "");
            Connections = (await connections.ListAsync()).Where(x => x.Enabled).ToArray();
            Conversations = await store.ListAsync(projectId);
            Memory = await store.GetMemoryAsync(projectId);
            SelectDefaultModel();
            var active = Conversations.FirstOrDefault(x => x.Status is BuildRunStatus.Queued or BuildRunStatus.Running);
            if (conversationId is { } id) Conversation = await store.LoadAsync(projectId, id);
            else if (active is not null) Conversation = await store.LoadAsync(projectId, active.Id);
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Loading = false; }
        _pollCancel = new();
        _poll = PollAsync(_pollCancel.Token);
    }
    private void SelectDefaultModel()
    {
        var connection = Connections.Where(x => x.Models.Count > 0)
            .OrderByDescending(x => x.IsLocalCli || (Uri.TryCreate(x.BaseUrl, UriKind.Absolute, out var url) && url.IsLoopback))
            .FirstOrDefault();
        Conversation.ConnectionId = connection?.Id ?? Guid.Empty;
        Conversation.Model = connection?.Models.FirstOrDefault() ?? "";
    }
    public void SelectConnection(string? value)
    {
        if (Locked || !Guid.TryParse(value, out var id)) return;
        Conversation.ConnectionId = id; Conversation.Model = Models.FirstOrDefault() ?? "";
    }
    public async Task NewAsync()
    {
        if (NavigationLocked) return;
        await StopSpeechAsync();
        Conversation = new() { ProjectId = Conversation.ProjectId, ConnectionId = Conversation.ConnectionId, Model = Conversation.Model };
        Draft = ""; Images.Clear(); Error = Notice = null; ConfirmDelete = false; ShowHistory = false;
    }
    public async Task OpenAsync(Guid id)
    {
        if (NavigationLocked) return;
        try
        {
            await StopSpeechAsync();
            Conversation = await store.LoadAsync(Conversation.ProjectId, id);
            Draft = ""; Images.Clear(); Error = Notice = null; ConfirmDelete = false; ShowHistory = false;
        }
        catch (Exception ex) { Error = ex.Message; }
    }
    public async Task DeleteAsync()
    {
        if (Locked) return;
        try { await store.DeleteAsync(Conversation.ProjectId, Conversation.Id); await NewAsync(); Conversations = await store.ListAsync(Conversation.ProjectId); }
        catch (Exception ex) { Error = ex.Message; }
    }
    public async Task SaveMemoryAsync()
    {
        if (Locked) return;
        try { await store.SaveMemoryAsync(Conversation.ProjectId, Memory); Notice = "Memory saved for your chats in this project."; Error = null; }
        catch (Exception ex) { Error = ex.Message; }
    }
    public async Task UploadAsync(InputFileChangeEventArgs args)
    {
        if (Locked) return;
        Uploading = true;
        var projectId = Conversation.ProjectId;
        var conversationId = Conversation.Id;
        try
        {
            var files = args.GetMultipleFiles(4);
            if (Images.Count + files.Count > 4) throw new ArgumentException("Attach up to four images.");
            var additions = new List<BuildImage>();
            foreach (var file in files)
            {
                await using var input = file.OpenReadStream(2_000_000);
                using var buffer = new MemoryStream();
                await input.CopyToAsync(buffer);
                var image = new BuildImage(file.Name, file.ContentType, buffer.ToArray()); image.Validate(); additions.Add(image);
            }
            if (Conversation.ProjectId == projectId && Conversation.Id == conversationId)
            { Images.AddRange(additions); Error = null; }
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Uploading = false; }
    }
    public Task SendAsync()
    {
        if (!CanSend) return Task.CompletedTask;
        _send = SendCoreAsync(); return _send;
    }
    public bool CanAnswer(JsonNode message) => !Locked && !Listening && Draft.Length == 0 && Images.Count == 0
        && Conversation.ConnectionId != Guid.Empty && Models.Contains(Conversation.Model)
        && ReferenceEquals(Conversation.Messages.LastOrDefault(), message);

    public Task AnswerAsync(JsonNode message, string answer)
    {
        if (!CanAnswer(message) || !SuggestedAnswers(message).Contains(answer)) return Task.CompletedTask;
        Draft = answer;
        return SendAsync();
    }
    private async Task SendCoreAsync()
    {
        Submitting = true; Error = Notice = null;
        var text = Draft; var images = Images.ToArray();
        try
        {
            await agent.QueueAsync(Conversation, text, images);
            Draft = ""; Images.Clear();
            Notice = "Working in the background. You can leave this page and return later.";
            Conversations = await store.ListAsync(Conversation.ProjectId);
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Submitting = false; NotifyStateChanged(); }
    }
    public async Task StopAsync()
    {
        try { await store.RequestStopAsync(Conversation.ProjectId, Conversation.Id); Notice = "Stop requested. Completed actions remain applied."; }
        catch (Exception ex) { Error = ex.Message; }
    }
    private async Task PollAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        var ticks = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (Submitting || Loading) continue;
                try
                {
                    var current = Conversation;
                    var chats = await store.ListAsync(current.ProjectId, ct);
                    if (Conversation != current) continue;
                    Conversations = chats;
                    var saved = chats.FirstOrDefault(x => x.Id == current.Id);
                    if (saved is not null && (Busy || saved.Status != current.Status))
                    {
                        var latest = await store.LoadAsync(current.ProjectId, current.Id, ct);
                        if (Conversation != current) continue;
                        Conversation = latest;
                        if (!Busy)
                        {
                            Notice = latest.Status == BuildRunStatus.Completed ? "Background run completed." : null;
                            Error = latest.Error;
                            Memory = await store.GetMemoryAsync(current.ProjectId, ct);
                        }
                    }
                    if (++ticks % 15 == 0 && !Locked)
                    {
                        Connections = (await connections.ListAsync(ct)).Where(x => x.Enabled).ToArray();
                        if (Conversation.ConnectionId == Guid.Empty) SelectDefaultModel();
                        else if (string.IsNullOrEmpty(Conversation.Model)) Conversation.Model = Models.FirstOrDefault() ?? "";
                    }
                    NotifyStateChanged();
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex) { Error = ex.Message; NotifyStateChanged(); }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
    private async Task StopPollingAsync()
    {
        if (_pollCancel is null) return;
        await _pollCancel.CancelAsync();
        if (_poll is not null) await _poll;
        _pollCancel.Dispose(); _pollCancel = null; _poll = null;
    }
    public static string Role(JsonNode message) => message["role"]?.GetValue<string>() ?? "assistant";
    public static string Text(JsonNode message) => message["content"] is JsonArray parts
        ? string.Join("\n", parts.Where(x => x?["type"]?.GetValue<string>() == "text").Select(x => x!["text"]!.GetValue<string>()))
        : message["content"]?.GetValue<string>() ?? "";
    public static IEnumerable<string> ImageUrls(JsonNode message) => (message["content"] as JsonArray ?? [])
        .Where(x => x?["type"]?.GetValue<string>() == "image_url").Select(x => x!["image_url"]!["url"]!.GetValue<string>());
    public static IEnumerable<JsonNode> Calls(JsonNode message) => (message["tool_calls"] as JsonArray ?? []).OfType<JsonNode>();
    public static IEnumerable<string> SuggestedAnswers(JsonNode message) => Role(message) == "assistant"
        ? (message["suggested_answers"] as JsonArray ?? []).OfType<JsonValue>()
            .Select(x => x.TryGetValue<string>(out var value) ? value : "").Where(x => !string.IsNullOrWhiteSpace(x))
        : [];

    public async Task InitializeBrowserAsync()
    {
        try
        {
            _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./build-chat.js");
            _reference ??= DotNetObjectReference.Create(this);
            SpeechSupported = await _module.InvokeAsync<bool>("supportsSpeech");
            await _module.InvokeVoidAsync("mountChat", ViewId); NotifyStateChanged();
        }
        catch (JSException) { SpeechSupported = false; }
    }
    public async Task ToggleSpeechAsync()
    {
        if (Busy || _module is null) return;
        try
        {
            if (Listening) await StopSpeechAsync();
            else { Listening = true; Error = null; await _module.InvokeVoidAsync("startSpeech", _reference, ViewId); }
        }
        catch (JSException) { Listening = false; Error = "Could not start dictation. Check microphone permission and browser speech support."; }
    }
    private async Task StopSpeechAsync()
    {
        if (_module is not null) await _module.InvokeVoidAsync("stopSpeech", ViewId);
        Listening = false;
    }
    [JSInvokable] public Task SpeechText(string text)
    {
        if (Listening && !Busy)
        {
            var transcript = (Draft + " " + text).Trim();
            Draft = transcript[..Math.Min(16000, transcript.Length)];
        }
        NotifyStateChanged(); return Task.CompletedTask;
    }
    [JSInvokable] public Task SpeechEnded(string? error)
    { Listening = false; if (error is not null) Error = error; NotifyStateChanged(); return Task.CompletedTask; }
    public async ValueTask DisposePageAsync()
    {
        Detach();
        await StopPollingAsync();
        if (_send is not null) await _send;
        try { if (_module is not null) { await _module.InvokeVoidAsync("unmountChat", ViewId); await _module.DisposeAsync(); } }
        catch (JSDisconnectedException) { }
        _module = null; _reference?.Dispose(); _reference = null; Listening = false;
        Conversation = new();
    }
}
