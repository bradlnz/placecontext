using System.Text.Json;
using Microsoft.AspNetCore.Components;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Host.Components.ViewModels.Helpers;
using PlaceContext.Infrastructure.Operations;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobChainsViewModel
{
    // ── Editor state ──────────────────────────────────────────────────────────────────────────
    public bool ShowEditor { get; private set; }
    public bool Saving { get; private set; }
    public Guid? EditChainId { get; private set; }
    public string? EditorError { get; private set; }
    public string EditorTab { get; set; } = "details";
    public string EditorView { get; set; } = "canvas";
    public string EdName { get; set; } = "";
    public string EdDescription { get; set; } = "";
    public List<List<Guid>> EdStages { get; } = new();
    public string EdAddJobId { get; set; } = "";

    /// <summary>Gates keyed by stage index (the stage after the gate). null = no gate.</summary>
    public Dictionary<int, ChainGate?> EdStageGates { get; } = new();
    public Dictionary<int, ChainAction?> EdStageActions { get; } = new();

    // ── Send Email action editor ─────────────────────────────────────────────────────────────
    public bool ShowEmailActionEditor { get; private set; }
    public int? EmailActionEditorStageIndex { get; private set; }
    public string EmailRecipient { get; set; } = "";
    public string EmailRecipientName { get; set; } = "";
    public string EmailSubject { get; set; } = "";
    public string EmailBody { get; set; } = "";
    public string EmailAttachmentPath { get; set; } = "";
    public string? EmailActionError { get; private set; }
    public bool LoadingEmailJsonPaths { get; private set; }
    public string? EmailJsonPathSource { get; private set; }
    public IReadOnlyList<EmailJsonPathSuggestion> EmailJsonPaths { get; private set; } =
        Array.Empty<EmailJsonPathSuggestion>();
    public bool ShowSmsActionEditor { get; private set; }
    public int? SmsActionEditorStageIndex { get; private set; }
    public string SmsRecipient { get; set; } = "";
    public string SmsBody { get; set; } = "";
    public string? SmsActionError { get; private set; }
    public bool ShowStageNodePicker { get; private set; }
    public int StageNodePickerIndex { get; private set; }
    private int? _actionInsertIndex;

    // ── Gate editor modal state ───────────────────────────────────────────────────────────────
    public bool ShowGateEditor { get; private set; }
    public int GateEditorStageIndex { get; private set; }
    public string GateEditorType { get; set; } = "wait"; // "wait" or "condition"
    public double GateEditorDuration { get; set; } = 30;
    public string GateEditorExpression { get; set; } = "exists:data";
    public string GateEditorOperator { get; set; } = "exists";
    public string GateEditorPath { get; set; } = "data";
    public string GateEditorValue { get; set; } = "";

    // ── Editor ────────────────────────────────────────────────────────────────────────────────
    public void NewChain()
    {
        EditChainId = null;
        EdName = "";
        EdDescription = "";
        EdStages.Clear();
        EdAddJobId = "";
        EdBranchJobIds.Clear();
        EdStageGates.Clear();
        EdStageActions.Clear();
        EditorError = null;
        EditorTab = "details";
        EditorView = "canvas";
        ShowEditor = true;
        NotifyStateChanged();
    }

    public async Task OpenChainAsync(JobChainView chain)
    {
        OpenChainEditor(chain);
        await SwitchToRunsTabAsync();
    }

    public void OpenChainEditor(JobChainView chain)
    {
        EditChainId = chain.Id;
        EdName = chain.Name;
        EdDescription = chain.Description ?? "";
        EdStages.Clear();
        EdStageGates.Clear();
        EdStageActions.Clear();
        foreach (var (stage, i) in chain.Stages.Select((stage, i) => (stage, i)))
        {
            EdStages.Add(stage.Jobs.Select(j => j.JobId).ToList());
            if (stage.Action is SendEmailChainActionView email)
                EdStageActions[i] = new SendEmailChainAction(
                    email.Recipient,
                    email.RecipientName,
                    email.Subject,
                    email.Body,
                    email.AttachmentPath
                );
            else if (stage.Action is SendSmsChainActionView sms)
                EdStageActions[i] = new SendSmsChainAction(sms.Recipient, sms.Body);
        }
        foreach (var (stage, i) in chain.Stages.Select((s, i) => (s, i)))
        {
            EdStageGates[i] = FromViewGate(stage.Gate);
        }
        EdAddJobId = "";
        EdBranchJobIds.Clear();
        EditorError = null;
        EditorTab = "details";
        EditorView = "canvas";
        OpenRun = null;
        StepRunDetail = null;
        ShowEditor = true;
        NotifyStateChanged();
    }

    public void CloseEditor()
    {
        ShowEditor = false;
        ShowGateEditor = false;
        ShowEmailActionEditor = false;
        ShowSmsActionEditor = false;
        ShowStageNodePicker = false;
        EditorError = null;
        OpenRun = null;
        StepRunDetail = null;
        StopPolling();
        NotifyStateChanged();
    }

    public void AddStage()
    {
        if (!Guid.TryParse(EdAddJobId, out var jobId))
            return;
        EdStages.Add(new List<Guid> { jobId });
        EdAddJobId = "";
        NotifyStateChanged();
    }

    public void AddStage(Guid jobId)
    {
        if (jobId == Guid.Empty)
            return;
        EdStages.Add(new List<Guid> { jobId });
        NotifyStateChanged();
    }

    public Dictionary<int, string> EdBranchJobIds { get; } = new();

    public void SetBranchJobId(int stageIndex, ChangeEventArgs args) =>
        EdBranchJobIds[stageIndex] = args.Value as string ?? string.Empty;

    public void AddBranch(int stageIndex)
    {
        if (
            stageIndex >= 0
            && stageIndex < EdStages.Count
            && EdBranchJobIds.TryGetValue(stageIndex, out var selectedId)
            && Guid.TryParse(selectedId, out var jobId)
        )
        {
            EdStages[stageIndex].Add(jobId);
            EdBranchJobIds.Remove(stageIndex);
            NotifyStateChanged();
        }
    }

    public void RemoveBranch(int stageIndex, int branchIndex)
    {
        if (
            stageIndex >= 0
            && stageIndex < EdStages.Count
            && branchIndex >= 0
            && branchIndex < EdStages[stageIndex].Count
        )
        {
            EdStages[stageIndex].RemoveAt(branchIndex);
            if (EdStages[stageIndex].Count == 0)
                RemoveStage(stageIndex);
            else
                NotifyStateChanged();
        }
    }

    public void MoveStage(int index, int delta)
    {
        var target = index + delta;
        if (target < 0 || target >= EdStages.Count)
            return;
        (EdStages[index], EdStages[target]) = (EdStages[target], EdStages[index]);
        var sourceGate = EdStageGates.GetValueOrDefault(index);
        var targetGate = EdStageGates.GetValueOrDefault(target);
        var sourceAction = EdStageActions.GetValueOrDefault(index);
        var targetAction = EdStageActions.GetValueOrDefault(target);
        SetGate(index, targetGate);
        SetGate(target, sourceGate);
        SetAction(index, targetAction);
        SetAction(target, sourceAction);
        NotifyStateChanged();
    }

    public void MoveStageTo(int sourceIndex, int targetIndex)
    {
        if (
            sourceIndex < 0
            || sourceIndex >= EdStages.Count
            || targetIndex < 0
            || targetIndex >= EdStages.Count
            || sourceIndex == targetIndex
        )
            return;

        var stage = EdStages[sourceIndex];
        var gate = EdStageGates.GetValueOrDefault(sourceIndex);
        var action = EdStageActions.GetValueOrDefault(sourceIndex);
        EdStages.RemoveAt(sourceIndex);
        ShiftGatesAfterRemoval(sourceIndex);
        ShiftActionsAfterRemoval(sourceIndex);
        if (targetIndex > sourceIndex)
            targetIndex--;
        EdStages.Insert(targetIndex, stage);
        ShiftGatesForInsert(targetIndex);
        ShiftActionsForInsert(targetIndex);
        SetGate(targetIndex, gate);
        SetAction(targetIndex, action);
        NotifyStateChanged();
    }

    public void RemoveStage(int stageIndex)
    {
        if (stageIndex < 0 || stageIndex >= EdStages.Count)
            return;
        EdStages.RemoveAt(stageIndex);
        ShiftGatesAfterRemoval(stageIndex);
        ShiftActionsAfterRemoval(stageIndex);
        NotifyStateChanged();
    }

    public void AddPath(int stageIndex, Guid jobId)
    {
        if (stageIndex < 0 || stageIndex >= EdStages.Count || jobId == Guid.Empty)
            return;
        EdStages[stageIndex].Add(jobId);
        NotifyStateChanged();
    }

    public void MovePath(int sourceStage, int sourceBranch, int targetStage)
    {
        if (
            sourceStage < 0
            || sourceStage >= EdStages.Count
            || targetStage < 0
            || targetStage >= EdStages.Count
            || sourceBranch < 0
            || sourceBranch >= EdStages[sourceStage].Count
            || sourceStage == targetStage
        )
            return;

        var jobId = EdStages[sourceStage][sourceBranch];
        EdStages[sourceStage].RemoveAt(sourceBranch);
        EdStages[targetStage].Add(jobId);
        if (EdStages[sourceStage].Count == 0)
            RemoveStage(sourceStage);
        else
            NotifyStateChanged();
    }

    private void ShiftGatesAfterRemoval(int removedIndex)
    {
        var shifted = EdStageGates
            .Where(kv => kv.Key != removedIndex)
            .ToDictionary(kv => kv.Key > removedIndex ? kv.Key - 1 : kv.Key, kv => kv.Value);
        EdStageGates.Clear();
        foreach (var (key, value) in shifted)
            EdStageGates[key] = value;
    }

    private void ShiftGatesForInsert(int insertedIndex)
    {
        var shifted = EdStageGates.OrderByDescending(kv => kv.Key).ToList();
        EdStageGates.Clear();
        foreach (var (key, value) in shifted)
            EdStageGates[key >= insertedIndex ? key + 1 : key] = value;
    }

    private void SetGate(int index, ChainGate? gate)
    {
        if (gate is null)
            EdStageGates.Remove(index);
        else
            EdStageGates[index] = gate;
    }

    private void SetAction(int index, ChainAction? action)
    {
        if (action is null)
            EdStageActions.Remove(index);
        else
            EdStageActions[index] = action;
    }

    private void ShiftActionsAfterRemoval(int removedIndex)
    {
        var shifted = EdStageActions
            .Where(kv => kv.Key != removedIndex)
            .ToDictionary(kv => kv.Key > removedIndex ? kv.Key - 1 : kv.Key, kv => kv.Value);
        EdStageActions.Clear();
        foreach (var (key, value) in shifted)
            EdStageActions[key] = value;
    }

    private void ShiftActionsForInsert(int insertedIndex)
    {
        var shifted = EdStageActions.OrderByDescending(kv => kv.Key).ToList();
        EdStageActions.Clear();
        foreach (var (key, value) in shifted)
            EdStageActions[key >= insertedIndex ? key + 1 : key] = value;
    }

    public async Task OpenNewEmailAction()
    {
        if (!CanSendEmailAction)
            return;
        _actionInsertIndex = null;
        await OpenEmailActionFormAsync(EdStages.Count);
    }

    public async Task OpenEmailActionAt(int stageIndex)
    {
        if (!CanSendEmailAction)
            return;
        _actionInsertIndex = stageIndex;
        await OpenEmailActionFormAsync(stageIndex);
    }

    private async Task OpenEmailActionFormAsync(int stageIndex)
    {
        EmailActionEditorStageIndex = null;
        EmailRecipient = "";
        EmailRecipientName = "";
        EmailSubject = "";
        EmailBody = "";
        EmailAttachmentPath = "";
        EmailActionError = null;
        ShowEmailActionEditor = true;
        NotifyStateChanged();
        await LoadEmailJsonPathsAsync(stageIndex);
    }

    public async Task OpenEmailActionEditor(int stageIndex)
    {
        if (
            !CanSendEmailAction
            || EdStageActions.GetValueOrDefault(stageIndex) is not SendEmailChainAction email
        )
            return;
        _actionInsertIndex = null;
        EmailActionEditorStageIndex = stageIndex;
        EmailRecipient = email.Recipient;
        EmailRecipientName = email.RecipientName;
        EmailSubject = email.Subject;
        EmailBody = email.Body;
        EmailAttachmentPath = email.AttachmentPath;
        EmailActionError = null;
        ShowEmailActionEditor = true;
        NotifyStateChanged();
        await LoadEmailJsonPathsAsync(stageIndex, autoFill: false);
    }

    public void SaveEmailAction()
    {
        EmailActionError = null;
        try
        {
            var action = new SendEmailChainAction(
                EmailRecipient,
                EmailRecipientName,
                EmailSubject,
                EmailBody,
                EmailAttachmentPath
            );
            var stageIndex = EmailActionEditorStageIndex;
            if (stageIndex is null)
            {
                stageIndex = _actionInsertIndex ?? EdStages.Count;
                ShiftGatesForInsert(stageIndex.Value);
                ShiftActionsForInsert(stageIndex.Value);
                EdStages.Insert(stageIndex.Value, new List<Guid>());
            }
            EdStageActions[stageIndex.Value] = action;
            ShowEmailActionEditor = false;
            NotifyStateChanged();
        }
        catch (Exception ex)
        {
            EmailActionError = ex.Message;
            NotifyStateChanged();
        }
    }

    public void CancelEmailActionEditor()
    {
        ShowEmailActionEditor = false;
        EmailActionError = null;
        NotifyStateChanged();
    }

    public void InsertEmailJsonPath(string path, string target)
    {
        var template = $"{{{{{path}}}}}";
        switch (target)
        {
            case "recipient":
                EmailRecipient = template;
                break;
            case "name":
                EmailRecipientName = template;
                break;
            case "subject":
                EmailSubject = AppendTemplate(EmailSubject, template);
                break;
            case "attachment":
                EmailAttachmentPath = template;
                break;
            default:
                EmailBody = AppendTemplate(EmailBody, template);
                break;
        }
        NotifyStateChanged();
    }

    private async Task LoadEmailJsonPathsAsync(int actionStageIndex, bool autoFill = true)
    {
        LoadingEmailJsonPaths = true;
        EmailJsonPaths = Array.Empty<EmailJsonPathSuggestion>();
        EmailJsonPathSource = null;
        NotifyStateChanged();
        try
        {
            var previousStageIndex = actionStageIndex - 1;
            // Message actions preserve the payload, so walk back to the nearest job stage.
            while (previousStageIndex >= 0 && EdStages[previousStageIndex].Count == 0)
                previousStageIndex--;
            if (previousStageIndex < 0)
                return;

            var jobIds = EdStages[previousStageIndex];
            var suggestions = new List<EmailJsonPathSuggestion>();
            var sourceNames = new List<string>();
            foreach (var (jobId, branchIndex) in jobIds.Select((id, index) => (id, index)))
            {
                var runs = await _svc.ListJobRunsAsync(jobId);
                var latest = runs.FirstOrDefault(run => run.Status is "Succeeded" or "Partial");
                if (latest is null)
                    continue;
                var detail = await _svc.GetJobRunAsync(latest.Id);
                var sample = LatestJsonOutput(detail);
                if (sample is null)
                    continue;
                using var document = JsonDocument.Parse(sample);
                var prefix = jobIds.Count > 1 ? branchIndex.ToString() : "";
                FlattenJsonPaths(document.RootElement, prefix, suggestions, depth: 0);
                sourceNames.Add(JobName(jobId));
            }

            EmailJsonPaths = suggestions
                .DistinctBy(item => item.Path, StringComparer.Ordinal)
                .Take(80)
                .ToList();
            if (sourceNames.Count > 0)
                EmailJsonPathSource =
                    $"Latest successful output from {string.Join(", ", sourceNames.Distinct())}";
            if (autoFill)
                AutoFillDetectedEmailPaths();
        }
        catch (JsonException)
        {
            EmailJsonPathSource = "The latest previous-step output is not JSON.";
        }
        catch (Exception ex)
        {
            EmailJsonPathSource =
                $"Could not inspect the latest previous-step output: {ex.Message}";
        }
        finally
        {
            LoadingEmailJsonPaths = false;
            NotifyStateChanged();
        }
    }

    private void AutoFillDetectedEmailPaths()
    {
        var email = EmailJsonPaths.FirstOrDefault(item =>
            LastPathPart(item.Path).Contains("email", StringComparison.OrdinalIgnoreCase)
        );
        var name = EmailJsonPaths.FirstOrDefault(item =>
            LastPathPart(item.Path) is "name" or "recipientName" or "clientName"
        );
        var attachment = EmailJsonPaths.FirstOrDefault(item => item.IsAttachmentCandidate);
        if (email is not null && string.IsNullOrWhiteSpace(EmailRecipient))
            EmailRecipient = $"{{{{{email.Path}}}}}";
        if (name is not null && string.IsNullOrWhiteSpace(EmailRecipientName))
            EmailRecipientName = $"{{{{{name.Path}}}}}";
        if (attachment is not null && string.IsNullOrWhiteSpace(EmailAttachmentPath))
            EmailAttachmentPath = $"{{{{{attachment.Path}}}}}";
    }

    private static void FlattenJsonPaths(
        JsonElement element,
        string path,
        ICollection<EmailJsonPathSuggestion> result,
        int depth
    )
    {
        if (depth > 8 || result.Count >= 80)
            return;
        if (IsAttachmentValue(element) && path.Length > 0)
            result.Add(new EmailJsonPathSuggestion(path, "attachment", true));

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
                FlattenJsonPaths(property.Value, JoinPath(path, property.Name), result, depth + 1);
            return;
        }
        if (element.ValueKind == JsonValueKind.Array)
        {
            // A representative first element describes the paths available from homogeneous output.
            if (element.GetArrayLength() > 0)
                FlattenJsonPaths(element[0], JoinPath(path, "0"), result, depth + 1);
            return;
        }
        if (path.Length == 0)
            return;
        result.Add(new EmailJsonPathSuggestion(path, Preview(element), false));
    }

    private static bool IsAttachmentValue(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
            return element.GetArrayLength() > 0 && IsAttachmentObject(element[0]);
        return IsAttachmentObject(element);
    }

    private static bool IsAttachmentObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return false;
        var names = element
            .EnumerateObject()
            .Select(property => property.Name.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        return (names.Contains("name") || names.Contains("filename"))
            && (
                names.Contains("content")
                || names.Contains("data")
                || names.Contains("contentbase64")
                || names.Contains("base64")
            );
    }

    private static string? LatestJsonOutput(JobRunDetailView? detail)
    {
        if (detail?.ReduceResult?.Artifact is { Length: > 0 } reduce)
            return reduce;
        return detail
            ?.ShardResults.OrderBy(shard => shard.Index)
            .Select(shard =>
                shard.Artifact
                ?? shard.Artifacts.FirstOrDefault(artifact => !artifact.IsBinary)?.Content
            )
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static string JoinPath(string prefix, string part) =>
        prefix.Length == 0 ? part : $"{prefix}.{part}";

    private static string LastPathPart(string path) =>
        path.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? path;

    private static string Preview(JsonElement value)
    {
        var text =
            value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
        return text.Length <= 42 ? text : text[..39] + "…";
    }

    private static string AppendTemplate(string current, string template) =>
        string.IsNullOrWhiteSpace(current) ? template : $"{current} {template}";

    public sealed record EmailJsonPathSuggestion(
        string Path,
        string Preview,
        bool IsAttachmentCandidate
    );

    public void OpenNewSmsAction()
    {
        if (!CanSendSmsAction)
            return;
        _actionInsertIndex = null;
        OpenSmsActionForm();
    }

    public void OpenSmsActionAt(int stageIndex)
    {
        if (!CanSendSmsAction)
            return;
        _actionInsertIndex = stageIndex;
        OpenSmsActionForm();
    }

    private void OpenSmsActionForm()
    {
        SmsActionEditorStageIndex = null;
        SmsRecipient = "";
        SmsBody = "";
        SmsActionError = null;
        ShowSmsActionEditor = true;
        NotifyStateChanged();
    }

    public void OpenSmsActionEditor(int stageIndex)
    {
        if (
            !CanSendSmsAction
            || EdStageActions.GetValueOrDefault(stageIndex) is not SendSmsChainAction sms
        )
            return;
        _actionInsertIndex = null;
        SmsActionEditorStageIndex = stageIndex;
        SmsRecipient = sms.Recipient;
        SmsBody = sms.Body;
        SmsActionError = null;
        ShowSmsActionEditor = true;
        NotifyStateChanged();
    }

    public void SaveSmsAction()
    {
        SmsActionError = null;
        try
        {
            var action = new SendSmsChainAction(SmsRecipient, SmsBody);
            var stageIndex = SmsActionEditorStageIndex;
            if (stageIndex is null)
            {
                stageIndex = _actionInsertIndex ?? EdStages.Count;
                ShiftGatesForInsert(stageIndex.Value);
                ShiftActionsForInsert(stageIndex.Value);
                EdStages.Insert(stageIndex.Value, new List<Guid>());
            }
            EdStageActions[stageIndex.Value] = action;
            ShowSmsActionEditor = false;
            NotifyStateChanged();
        }
        catch (Exception ex)
        {
            SmsActionError = ex.Message;
            NotifyStateChanged();
        }
    }

    public void CancelSmsActionEditor()
    {
        ShowSmsActionEditor = false;
        SmsActionError = null;
        NotifyStateChanged();
    }

    public void OpenStageNodePicker(int stageIndex)
    {
        StageNodePickerIndex = stageIndex;
        ShowStageNodePicker = true;
        NotifyStateChanged();
    }

    public void CloseStageNodePicker()
    {
        ShowStageNodePicker = false;
        NotifyStateChanged();
    }

    public async Task ChooseStageNode(string type)
    {
        ShowStageNodePicker = false;
        switch (type)
        {
            case "email":
                await OpenEmailActionAt(StageNodePickerIndex);
                break;
            case "sms":
                OpenSmsActionAt(StageNodePickerIndex);
                break;
            case "condition":
                OpenGateEditor(StageNodePickerIndex);
                GateEditorType = "condition";
                break;
            default:
                OpenGateEditor(StageNodePickerIndex);
                GateEditorType = "wait";
                break;
        }
        NotifyStateChanged();
    }

    // ── Gate editing ──────────────────────────────────────────────────────────────────────────

    /// <summary>Open the gate editor for the stage after the gate at the given source index.</summary>
    public void OpenGateEditor(int stageIndex)
    {
        GateEditorStageIndex = stageIndex;
        var existing = EdStageGates.GetValueOrDefault(stageIndex);
        switch (existing)
        {
            case WaitGate w:
                GateEditorType = "wait";
                GateEditorDuration = w.Duration.TotalSeconds;
                GateEditorExpression = "exists:data";
                GateEditorOperator = "exists";
                GateEditorPath = "data";
                GateEditorValue = "";
                break;
            case ConditionGate c:
                GateEditorType = "condition";
                GateEditorExpression = c.Expression;
                ParseCondition(c.Expression);
                GateEditorDuration = 30;
                break;
            default:
                GateEditorType = "wait";
                GateEditorDuration = 30;
                GateEditorExpression = "exists:data";
                GateEditorOperator = "exists";
                GateEditorPath = "data";
                GateEditorValue = "";
                break;
        }
        ShowGateEditor = true;
        NotifyStateChanged();
    }

    public void SaveGate()
    {
        if (GateEditorType == "condition")
            GateEditorExpression = BuildConditionExpression();
        EdStageGates[GateEditorStageIndex] = GateEditorType switch
        {
            "wait" => new WaitGate(TimeSpan.FromSeconds(GateEditorDuration)),
            "condition" => new ConditionGate(GateEditorExpression),
            _ => null,
        };
        ShowGateEditor = false;
        NotifyStateChanged();
    }

    public string ConditionPreview() => BuildConditionExpression();

    public bool ConditionNeedsValue =>
        GateEditorOperator is not ("exists" or "notexists" or "empty" or "notempty");

    private string BuildConditionExpression()
    {
        var path = string.IsNullOrWhiteSpace(GateEditorPath)
            ? "data"
            : GateEditorPath.Trim().TrimStart('$', '.');
        return ConditionNeedsValue
            ? $"{GateEditorOperator}:{path}:{GateEditorValue.Trim()}"
            : $"{GateEditorOperator}:{path}";
    }

    private void ParseCondition(string expression)
    {
        var parts = expression.Split(':', 3);
        GateEditorOperator = parts.Length > 0 && parts[0].Length > 0 ? parts[0] : "exists";
        GateEditorPath = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : "data";
        GateEditorValue = parts.Length > 2 ? parts[2] : "";
    }

    public void RemoveGate(int stageIndex)
    {
        EdStageGates.Remove(stageIndex);
        NotifyStateChanged();
    }

    public void CancelGateEditor()
    {
        ShowGateEditor = false;
        NotifyStateChanged();
    }

    /// <summary>Build the gates dictionary for the canvas (non-null entries only, keyed by stage index).</summary>
    public Dictionary<int, ChainGateView> BuildCanvasGates()
    {
        var result = new Dictionary<int, ChainGateView>();
        foreach (var (index, gate) in EdStageGates)
        {
            if (gate is not null)
            {
                var v = ToViewGate(gate);
                if (v is not null)
                    result[index] = v;
            }
        }
        return result;
    }

    /// <summary>Build a view model for the canvas from current editor state.</summary>
    public List<JobChainStageView> BuildCanvasStageViews()
    {
        var views = new List<JobChainStageView>(EdStages.Count);
        foreach (var (stage, i) in EdStages.Select((s, i) => (s, i)))
        {
            var stepViews = stage
                .Select(jobId => new JobChainStepView(jobId, JobName(jobId)))
                .ToList();
            var gate = EdStageGates.GetValueOrDefault(i);
            ChainActionView? action = EdStageActions.GetValueOrDefault(i) switch
            {
                SendEmailChainAction email => new SendEmailChainActionView(
                    email.Recipient,
                    email.RecipientName,
                    email.Subject,
                    email.Body,
                    email.AttachmentPath
                ),
                SendSmsChainAction sms => new SendSmsChainActionView(sms.Recipient, sms.Body),
                _ => null,
            };
            views.Add(new JobChainStageView(stepViews, ToViewGate(gate), Action: action));
        }
        return views;
    }

    // ── Save ──────────────────────────────────────────────────────────────────────────────────

    public async Task SaveChainAsync()
    {
        EditorError = null;
        if (string.IsNullOrWhiteSpace(EdName))
        {
            EditorError = "Name is required.";
            NotifyStateChanged();
            return;
        }

        var populatedStages = EdStages
            .Select((stage, index) => (Stage: stage, OriginalIndex: index))
            .Where(item =>
                item.Stage.Count > 0
                || EdStageActions.GetValueOrDefault(item.OriginalIndex) is not null
            )
            .ToList();
        var stages = populatedStages
            .Select(item => (IReadOnlyList<Guid>)item.Stage.ToList())
            .ToList();
        if (stages.Count == 0)
        {
            EditorError = "Add at least one step.";
            NotifyStateChanged();
            return;
        }
        var flatJobIds = populatedStages.SelectMany(item => item.Stage).ToList();
        var stageActions = populatedStages
            .Select(item => EdStageActions.GetValueOrDefault(item.OriginalIndex))
            .ToList();

        // Build the parallel gates list — one per stage, null when no gate is set.
        IReadOnlyList<ChainGate?>? stageGates = null;
        if (EdStageGates.Count > 0)
            stageGates = populatedStages
                .Select(item => EdStageGates.GetValueOrDefault(item.OriginalIndex))
                .ToList();

        Saving = true;
        try
        {
            if (EditChainId.HasValue)
                await _svc.UpdateJobChainAsync(
                    EditChainId.Value,
                    EdName.Trim(),
                    string.IsNullOrWhiteSpace(EdDescription) ? null : EdDescription.Trim(),
                    flatJobIds,
                    stages,
                    stageGates,
                    stageActions
                );
            else
                await _svc.CreateJobChainAsync(
                    ProjectId,
                    EdName.Trim(),
                    string.IsNullOrWhiteSpace(EdDescription) ? null : EdDescription.Trim(),
                    flatJobIds,
                    stages,
                    stageGates,
                    stageActions
                );

            Chains = await _svc.ListJobChainsAsync(ProjectId);
            ShowEditor = false;
            Message = EditChainId.HasValue
                ? $"Chain '{EdName.Trim()}' updated."
                : $"Chain '{EdName.Trim()}' created.";
        }
        catch (Exception ex)
        {
            EditorError = ex.Message;
        }
        finally
        {
            Saving = false;
            NotifyStateChanged();
        }
    }

    public async Task DeleteChainAsync(Guid chainId)
    {
        try
        {
            await _svc.DeleteJobChainAsync(chainId);
            Chains = await _svc.ListJobChainsAsync(ProjectId);
            ConfirmDeleteId = null;
            if (EditChainId == chainId)
                CloseEditor();
            Message = "Chain deleted.";
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        NotifyStateChanged();
    }

    // ── Gate view ↔ domain conversion ────────────────────────────────────────────────────────
    internal static ChainGateView? ToViewGate(ChainGate? gate) =>
        gate switch
        {
            null => null,
            NoGate => null,
            WaitGate w => new WaitGateView(w.Duration.TotalSeconds),
            ConditionGate c => new ConditionGateView(c.Expression),
            _ => null,
        };

    private static ChainGate? FromViewGate(ChainGateView? gate) =>
        gate switch
        {
            null => null,
            NoGateView => null,
            WaitGateView w => new WaitGate(TimeSpan.FromSeconds(w.DurationSeconds)),
            ConditionGateView c => new ConditionGate(c.Expression),
            _ => null,
        };
}
