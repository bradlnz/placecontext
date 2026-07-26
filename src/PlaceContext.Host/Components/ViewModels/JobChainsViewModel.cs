using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels.Helpers;
using PlaceContext.Infrastructure.Operations;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class JobChainsViewModel : PageViewModel
{
    private readonly IPlaceContextService _svc;
    private readonly OperationCenter _opCenter;

    public JobChainsViewModel(IPlaceContextService svc, OperationCenter opCenter)
    {
        _svc = svc;
        _opCenter = opCenter;
    }

    // ── Parameters ────────────────────────────────────────────────────────────────────────────
    public Guid ProjectId { get; private set; }

    // ── Chains state ──────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<JobChainView>? Chains { get; private set; }
    public IReadOnlyList<JobView>? Jobs { get; private set; }
    public string? Message { get; private set; }
    public bool Loading { get; private set; } = true;
    public Guid? ConfirmDeleteId { get; set; }

    // ── Editor state ──────────────────────────────────────────────────────────────────────────
    public bool ShowEditor { get; private set; }
    public bool Saving { get; private set; }
    public Guid? EditChainId { get; private set; }
    public string? EditorError { get; private set; }
    public string EditorTab { get; set; } = "details";
    public string EdName { get; set; } = "";
    public string EdDescription { get; set; } = "";
    public List<List<Guid>> EdStages { get; } = new();
    public string EdAddJobId { get; set; } = "";

    // ── Pipeline runs state ───────────────────────────────────────────────────────────────────
    public IReadOnlyList<ChainRunView>? ChainRuns { get; private set; }
    public ChainRunView? OpenRun { get; private set; }
    public JobRunDetailView? StepRunDetail { get; private set; }
    public bool FollowNewest { get; set; }
    private CancellationTokenSource? _pollCts;

    // ── Run-input prompt state ────────────────────────────────────────────────────────────────
    public JobChainView? RunPromptChain { get; private set; }
    public List<(int Index, JobView Job)> RunPromptSteps { get; } = new();
    public Dictionary<string, string> RunArgs { get; set; } = new();
    public string? RunPromptError { get; private set; }

    // ── Helpers used by markup ────────────────────────────────────────────────────────────────
    public static string ArgKey(int stepIndex, string param) => $"step{stepIndex}:{param}";

    public string JobName(Guid jobId) => Jobs?.FirstOrDefault(j => j.Id == jobId)?.Name ?? jobId.ToString("N")[..8];

    public static string StatusColor(string status) => StatusHelper.Color(status);
    public static string StatusBg(string status) => StatusHelper.Background(status);
    public static string FormatDuration(DateTimeOffset start, DateTimeOffset end) => FormatHelper.Duration(start, end);
    public static string PrettyJson(string raw) => FormatHelper.PrettyJson(raw);

    // ── Lifecycle ─────────────────────────────────────────────────────────────────────────────
    public void Initialize(Guid projectId)
    {
        ProjectId = projectId;
        _opCenter.Changed += OnOpsChanged;
    }

    public void DetachEvents()
    {
        _opCenter.Changed -= OnOpsChanged;
        StopPolling();
    }

    public async Task LoadAsync()
    {
        Loading = true;
        Message = null;
        try
        {
            Chains = await _svc.ListJobChainsAsync(ProjectId);
            Jobs = await _svc.ListJobsAsync(ProjectId);
        }
        catch (Exception ex) { Message = ex.Message; }
        finally { Loading = false; NotifyStateChanged(); }
    }

    private void OnOpsChanged()
    {
        if (OpenRun is { } run)
            _ = RefreshRunsAsync(run.ChainId, openNewest: false);
    }

    // ── Run chain ─────────────────────────────────────────────────────────────────────────────
    public async Task RunChainAsync(JobChainView chain)
    {
        var hasParamSteps = chain.Steps.Any(s =>
        {
            var job = Jobs?.FirstOrDefault(j => j.Id == s.JobId);
            return job is not null && job.Parameters.Count > 0;
        });

        if (hasParamSteps)
        {
            RunPromptChain = chain;
            RunPromptSteps.Clear();
            RunPromptSteps.AddRange(
                chain.Steps.Select((s, i) => (i, Jobs?.FirstOrDefault(j => j.Id == s.JobId)))
                    .Where(x => x.Item2 is not null).Select(x => (x.i, x.Item2!)));
            RunArgs = new Dictionary<string, string>();
            RunPromptError = null;
            // Prefill defaults
            foreach (var (idx, job) in RunPromptSteps)
                foreach (var p in job.Parameters)
                    RunArgs[ArgKey(idx, p.Name)] = StoredPayloadDefaults(job).GetValueOrDefault(p.Name, "");
            NotifyStateChanged();
            return;
        }
        await RunChainCoreAsync(chain, null);
    }

    public static Dictionary<string, string> StoredPayloadDefaults(JobView job)
    {
        var result = new Dictionary<string, string>();
        foreach (var payload in job.InputPayloads)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(payload);
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                    foreach (var prop in doc.RootElement.EnumerateObject())
                        if (prop.Value.ValueKind is System.Text.Json.JsonValueKind.String or System.Text.Json.JsonValueKind.Number or System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
                            result.TryAdd(prop.Name, prop.Value.ToString());
            }
            catch { }
        }
        return result;
    }

    public string GetArg(string name) => RunArgs.TryGetValue(name, out var v) ? v : "";
    public void SetArg(string name, string value) => RunArgs[name] = value;

    public async Task SubmitRunPromptAsync()
    {
        if (RunPromptChain is null) return;
        RunPromptError = null;

        var payloadDict = new Dictionary<string, string>();
        foreach (var (idx, job) in RunPromptSteps)
        {
            foreach (var p in job.Parameters)
            {
                var key = ArgKey(idx, p.Name);
                var val = GetArg(key);
                if (p.Required && string.IsNullOrWhiteSpace(val))
                {
                    RunPromptError = $"Required: {p.Label ?? p.Name} (step {idx + 1})";
                    NotifyStateChanged();
                    return;
                }
                payloadDict[key] = val;
            }
        }
        var payload = System.Text.Json.JsonSerializer.Serialize(payloadDict);
        await RunChainCoreAsync(RunPromptChain, payload);
    }

    public void CancelRunPrompt() { RunPromptChain = null; NotifyStateChanged(); }

    private async Task RunChainCoreAsync(JobChainView chain, string? payload, IReadOnlyDictionary<int, string>? stepOverrides = null)
    {
        Message = null;
        var tenant = CurrentTenant.Current;
        if (tenant is null) { Message = "No tenant resolved — sign in again."; NotifyStateChanged(); return; }
        var chainRunId = Guid.NewGuid();
        _opCenter.Run(tenant, ProjectId, $"Run chain — {chain.Name}", $"/project/{ProjectId}/chains",
            async (sp, ct) =>
            {
                var svc = sp.GetRequiredService<IPlaceContextService>();
                var result = await svc.RunJobChainAsync(chain.Id, payload, chainRunId, stepOverrides, ct);
                return $"chain finished — {result.Status}";
            });
        RunPromptChain = null;
        Message = $"Run of {chain.Name} started — follow it in the notifications bell.";
        StopPolling();
        OpenRun = null;
        await RefreshRunsAsync(chain.Id, openNewest: true);
    }

    // ── Runs tab ──────────────────────────────────────────────────────────────────────────────
    public async Task SwitchToRunsTabAsync()
    {
        EditorTab = "runs";
        StepRunDetail = null;
        if (EditChainId is { } chainId)
            await RefreshRunsAsync(chainId, openNewest: false);
    }

    public void OpenChainRun(ChainRunView run)
    {
        OpenRun = run;
        StepRunDetail = null;
        NotifyStateChanged();
    }

    public async Task OpenStepRunAsync(ChainStepRunView step)
    {
        try { if (step.RunId is { } rid) StepRunDetail = await _svc.GetJobRunAsync(rid); else StepRunDetail = null; }
        catch { StepRunDetail = null; }
        NotifyStateChanged();
    }

    public void CloseStepRun() { StepRunDetail = null; NotifyStateChanged(); }

    private async Task RefreshRunsAsync(Guid chainId, bool openNewest)
    {
        try
        {
            ChainRuns = await _svc.ListChainRunsAsync(chainId);
            if (openNewest && ChainRuns.Count > 0)
            {
                OpenRun = ChainRuns[0];
                FollowNewest = true;
                StartPolling();
            }
            else if (OpenRun is { } current)
            {
                var updated = ChainRuns.FirstOrDefault(r => r.Id == current.Id);
                if (updated is not null && updated.Status != current.Status)
                    OpenRun = updated;
            }
        }
        catch { }
        NotifyStateChanged();
    }

    private void StartPolling()
    {
        StopPolling();
        _pollCts = new CancellationTokenSource();
        var ct = _pollCts.Token;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(2000, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested) break;
                try
                {
                    if (OpenRun is { } run)
                        await RefreshRunsAsync(run.ChainId, openNewest: false);
                }
                catch { }
            }
        }, ct);
    }

    private void StopPolling()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
    }

    // ── Editor ────────────────────────────────────────────────────────────────────────────────
    public void NewChain()
    {
        EditChainId = null;
        EdName = "";
        EdDescription = "";
        EdStages.Clear();
        EdStages.Add(new List<Guid>());
        EdAddJobId = "";
        EditorError = null;
        EditorTab = "details";
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
        foreach (var stage in chain.Stages)
            EdStages.Add(stage.Jobs.Select(j => j.JobId).ToList());
        if (EdStages.Count == 0) EdStages.Add(new List<Guid>());
        EdAddJobId = "";
        EditorError = null;
        EditorTab = "details";
        OpenRun = null;
        StepRunDetail = null;
        ShowEditor = true;
        NotifyStateChanged();
    }

    public void CloseEditor()
    {
        ShowEditor = false;
        EditorError = null;
        OpenRun = null;
        StepRunDetail = null;
        StopPolling();
        NotifyStateChanged();
    }

    public void AddStage()
    {
        if (Guid.TryParse(EdAddJobId, out var jobId))
            EdStages.Add(new List<Guid> { jobId });
        else
            EdStages.Add(new List<Guid>());
        NotifyStateChanged();
    }

    public void AddBranch(int stageIndex)
    {
        if (stageIndex >= 0 && stageIndex < EdStages.Count && Guid.TryParse(EdAddJobId, out var jobId))
        {
            EdStages[stageIndex].Add(jobId);
            NotifyStateChanged();
        }
    }

    public void RemoveBranch(int stageIndex, int branchIndex)
    {
        if (stageIndex >= 0 && stageIndex < EdStages.Count
            && branchIndex >= 0 && branchIndex < EdStages[stageIndex].Count)
        {
            EdStages[stageIndex].RemoveAt(branchIndex);
            if (EdStages[stageIndex].Count == 0 && EdStages.Count > 1)
                EdStages.RemoveAt(stageIndex);
            NotifyStateChanged();
        }
    }

    public void MoveStage(int index, int delta)
    {
        var target = index + delta;
        if (target < 0 || target >= EdStages.Count) return;
        (EdStages[index], EdStages[target]) = (EdStages[target], EdStages[index]);
        NotifyStateChanged();
    }

    public async Task SaveChainAsync()
    {
        EditorError = null;
        if (string.IsNullOrWhiteSpace(EdName)) { EditorError = "Name is required."; NotifyStateChanged(); return; }

        var stages = EdStages.Where(s => s.Count > 0).Select(s => (IReadOnlyList<Guid>)s.ToList()).ToList();
        if (stages.Count == 0) { EditorError = "Add at least one step."; NotifyStateChanged(); return; }
        var flatJobIds = EdStages.SelectMany(s => s).ToList();

        Saving = true;
        try
        {
            if (EditChainId.HasValue)
                await _svc.UpdateJobChainAsync(EditChainId.Value, EdName.Trim(),
                    string.IsNullOrWhiteSpace(EdDescription) ? null : EdDescription.Trim(), flatJobIds, stages);
            else
                await _svc.CreateJobChainAsync(ProjectId, EdName.Trim(),
                    string.IsNullOrWhiteSpace(EdDescription) ? null : EdDescription.Trim(), flatJobIds, stages);

            Chains = await _svc.ListJobChainsAsync(ProjectId);
            ShowEditor = false;
            Message = EditChainId.HasValue ? $"Chain '{EdName.Trim()}' updated." : $"Chain '{EdName.Trim()}' created.";
        }
        catch (Exception ex) { EditorError = ex.Message; }
        finally { Saving = false; NotifyStateChanged(); }
    }

    public async Task DeleteChainAsync(Guid chainId)
    {
        try
        {
            await _svc.DeleteJobChainAsync(chainId);
            Chains = await _svc.ListJobChainsAsync(ProjectId);
            ConfirmDeleteId = null;
            if (EditChainId == chainId) CloseEditor();
            Message = "Chain deleted.";
        }
        catch (Exception ex) { Message = ex.Message; }
        NotifyStateChanged();
    }
}
