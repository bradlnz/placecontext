using System.Text.Json;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobChainsViewModel
{
    // ── Pipeline runs state ───────────────────────────────────────────────────────────────────
    public IReadOnlyList<ChainRunView>? ChainRuns
    {
        get; private set;
    }
    public ChainRunView? OpenRun
    {
        get; private set;
    }
    public JobRunDetailView? StepRunDetail
    {
        get; private set;
    }
    private int? _executionStepIndex;
    public ChainStepRunView? ExecutionStep => OpenRun?.Steps.FirstOrDefault(step => step.Index == _executionStepIndex);
    public IReadOnlyList<ChainStepRunView> ExecutionLogSteps => LogSteps.Where(step => step.Index == _executionStepIndex).ToArray();

    public async Task OpenExecutionStepAsync(ChainStepRunView step)
    {
        if (OpenRun?.Steps.Any(candidate => candidate.Index == step.Index) != true) return;
        _executionStepIndex = step.Index;
        SelectedStepRunId = null;
        StepRunDetail = null;
        StepOutputError = null;
        NotifyStateChanged();
        await RefreshStepOutputAsync(OpenRun);
        NotifyStateChanged();
    }

    public void CloseExecutionStep()
    {
        _executionStepIndex = null;
        NotifyStateChanged();
    }

    public Guid? SelectedStepRunId
    {
        get; private set;
    }
    public string? StepOutputError
    {
        get; private set;
    }
    public IReadOnlyList<(string Path, string Value)> ContextRows =>
        FlattenContext(OpenRun?.FinalOutput);
    public bool FollowNewest
    {
        get; set;
    }
    private CancellationTokenSource? _pollCts;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private static IReadOnlyList<(string Path, string Value)> FlattenContext(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<(string, string)>();
        }
        try
        {
            using var doc = JsonDocument.Parse(json);
            var rows = new List<(string, string)>();
            Flatten(doc.RootElement, "", rows);
            return rows.Take(250).ToList();
        }
        catch (JsonException)
        {
            return new[] { ("payload", json) };
        }
    }

    private static void Flatten(
        JsonElement node,
        string path,
        List<(string Path, string Value)> rows
    )
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in node.EnumerateObject())
            {
                Flatten(
                                property.Value,
                                string.IsNullOrEmpty(path) ? property.Name : $"{path}.{property.Name}",
                                rows
                            );
            }
            return;
        }
        if (node.ValueKind == JsonValueKind.Array)
        {
            rows.Add((path, node.GetRawText()));
            return;
        }
        rows.Add((path, node.ToString()));
    }

    // ── Run-input prompt state ────────────────────────────────────────────────────────────────
    public JobChainView? RunPromptChain
    {
        get; private set;
    }
    public List<(int Index, JobView Job)> RunPromptSteps { get; } = new();
    public Dictionary<string, string> RunArgs => _runPrompt.Args;
    public string? RunPromptError => _runPrompt.Error;
    public string RunAttachment { get; private set; } = "";

    private readonly ParameterPromptState _resumePrompt = new();
    private Guid? _resumeRequestId;
    public string? ResumeError
    {
        get; private set;
    }
    public bool Resuming
    {
        get; private set;
    }
    public bool? PendingHumanAnswer
    {
        get; private set;
    }
    private Guid? _pendingHumanRunId;
    private Guid? _pendingHumanRequestId;
    public bool HumanConfirmationCurrent => PendingHumanAnswer is not null
        && OpenRun is { } currentRun
        && currentRun.Id == _pendingHumanRunId
        && currentRun.InputRequest is { } request
        && request.Id == _pendingHumanRequestId
        && request.SubmittedValues is null
        && request.Kind == LoopChainAction.HumanType
        && request.ResponseType == LoopChainAction.YesNoResponse;

    public int RunsAwaitingInput => ChainRuns?.Count(run => run.Status == "AwaitingInput") ?? 0;
    public int RunsFailedOrPartial => ChainRuns?.Count(run => run.Status is "Failed" or "Partial") ?? 0;
    public int RunsSucceeded => ChainRuns?.Count(run => run.Status == "Succeeded") ?? 0;
    public static bool NeedsHumanInput(ChainRunView run) =>
        run.Status == "AwaitingInput" && run.InputRequest is { Kind: LoopChainAction.HumanType };

    public void RequestHumanAnswer(bool answer)
    {
        if (Resuming || _submissions is null || OpenRun is not { } run || !NeedsHumanInput(run)
            || run.InputRequest is not { ResponseType: LoopChainAction.YesNoResponse, SubmittedValues: null } request)
        {
            return;
        }
        PendingHumanAnswer = answer;
        _pendingHumanRunId = run.Id;
        _pendingHumanRequestId = request.Id;
        NotifyStateChanged();
    }

    public void CancelHumanAnswer()
    {
        if (Resuming)
        {
            return;
        }
        ClearHumanConfirmation();
        NotifyStateChanged();
    }

    public async Task ConfirmHumanAnswerAsync()
    {
        if (!HumanConfirmationCurrent || PendingHumanAnswer is not { } answer || Resuming)
        {
            return;
        }
        await ResumeWithInputsCoreAsync(answer);
        if (ResumeError is null)
        {
            ClearHumanConfirmation();
        }
        NotifyStateChanged();
    }

    private void ClearHumanConfirmation()
    {
        PendingHumanAnswer = null;
        _pendingHumanRunId = null;
        _pendingHumanRequestId = null;
    }

    public string GetResumeArg(int index, string name)
    {
        if (_resumeRequestId != OpenRun?.InputRequest?.Id)
        {
            _resumePrompt.Clear();
            _resumeRequestId = OpenRun?.InputRequest?.Id;
            ResumeError = null;
            foreach (var step in OpenRun?.InputRequest?.Steps ?? [])
            {
                foreach (var field in step.Parameters)
                {
                    _resumePrompt.Set(ArgKey(step.Index, field.Name), step.Values?.GetValueOrDefault(field.Name)?.ToString() ?? "");
                }
            }
        }
        return _resumePrompt.Get(ArgKey(index, name));
    }

    public void SetResumeArg(int index, string name, string value) => _resumePrompt.Set(ArgKey(index, name), value);

    public bool CanResumeAgentAutomatically
    {
        get
        {
            if (OpenRun is not { Status: "AwaitingInput", InputRequest: { Steps.Count: 1 } request } run) return false;
            var stageIndex = run.Steps.FirstOrDefault(step => step.Index == request.Steps[0].Index)?.StageIndex;
            var stages = Chains?.FirstOrDefault(chain => chain.Id == run.ChainId)?.Stages;
            return stageIndex is { } index && stages is not null && index >= 0 && index < stages.Count
                && stages[index].Action is LoopChainActionView { AgentProvider: LoopChainAction.AutomaticAgent };
        }
    }

    public async Task ResumeAgentAutomaticallyAsync()
    {
        if (!CanResumeAgentAutomatically || OpenRun?.InputRequest is not { } request || _submissions is null || Resuming) return;
        Resuming = true;
        ResumeError = null;
        try
        {
            await _submissions.ResumeAgentAsync(OpenRun.Id, request.Id);
            await RefreshRunsAsync(OpenRun.ChainId);
        }
        catch (Exception ex) { ResumeError = ex.Message; }
        finally { Resuming = false; NotifyStateChanged(); }
    }

    public Task ResumeWithInputsAsync() => ResumeWithInputsCoreAsync(null);
    public Task RespondYesNoAsync(bool answer) => ResumeWithInputsCoreAsync(answer);

    private async Task ResumeWithInputsCoreAsync(bool? answer)
    {
        if (OpenRun?.InputRequest is not { } request || _submissions is null || Resuming)
        {
            return;
        }
        Resuming = true;
        ResumeError = null;
        try
        {
            if (request.ResponseType == LoopChainAction.YesNoResponse && answer is null)
            {
                throw new ArgumentException("Choose Yes or No to continue.");
            }
            var values = request.Steps.ToDictionary(step => step.Index.ToString(), step =>
                            request.ResponseType == LoopChainAction.YesNoResponse
                                ? JsonSerializer.SerializeToElement(step.Parameters.ToDictionary(field => field.Name, _ => answer!.Value))
                                : JsonSerializer.Deserialize<JsonElement>(ParameterPromptState.ToJsonPayload(step.Parameters,
                                    name => GetResumeArg(step.Index, name))));
            await _submissions.ResumeAsync(OpenRun.Id, request.Id, JsonSerializer.Serialize(values));
            await RefreshRunsAsync(OpenRun.ChainId);
        }
        catch (Exception ex)
        {
            ResumeError = ex.Message;
        }
        finally
        {
            Resuming = false;
            NotifyStateChanged();
        }
    }

    // ── Run workflow ─────────────────────────────────────────────────────────────────────────────
    public Task RunChainAsync(JobChainView chain)
    {
        var plan = ChainParameterPromptPlan.Build(chain, Jobs ?? Array.Empty<JobView>());
        RunPromptChain = chain;
        RunPromptSteps.Clear();
        RunPromptSteps.AddRange(plan.Steps);
        _runPrompt.Reset(plan.Defaults);
        RunAttachment = "";
        NotifyStateChanged();
        return Task.CompletedTask;
    }

    public string GetArg(string name) => _runPrompt.Get(name);

    public void SetArg(string name, string value) => _runPrompt.Set(name, value);

    public void SetRunAttachment(string value) => RunAttachment = value;

    public async Task SubmitRunPromptAsync()
    {
        if (RunPromptChain is null)
        {
            return;
        }
        if (!_runPrompt.ValidateChainStepParameters(RunPromptSteps))
        {
            NotifyStateChanged();
            return;
        }

        // Form keys stay stepN:param; wire format is per-step bare-param JSON via StepPayloadOverrides.
        var overrides = _runPrompt.ToStepPayloadOverrides(RunPromptSteps);
        var payload = ParameterPromptState.WithChainAttachment(
            overrides.GetValueOrDefault(0),
            RunAttachment
        );
        await RunChainCoreAsync(RunPromptChain, payload, overrides);
    }

    public void CancelRunPrompt()
    {
        RunPromptChain = null;
        RunPromptSteps.Clear();
        _runPrompt.Clear();
        RunAttachment = "";
        NotifyStateChanged();
    }

    private async Task RunChainCoreAsync(
        JobChainView chain,
        string? payload,
        IReadOnlyDictionary<int, string>? stepOverrides = null
    )
    {
        Message = null;
        var chainRunId = Guid.NewGuid();
        if (_managedHosting)
        {
            try
            {
                if (_submissions is null)
                {
                    throw new InvalidOperationException("Managed workflow submissions are not configured.");
                }
                var receipt = await _submissions.EnqueueAsync(ProjectId, chain.Id, payload,
                                    idempotencyKey: "portal:" + chainRunId.ToString("N"),
                                    stepPayloadOverrides: stepOverrides);
                chainRunId = receipt.ChainRunId;
                _pendingSubmissionId = receipt.TrackingId;
            }
            catch (Exception ex)
            {
                Message = ex.Message;
                NotifyStateChanged();
                return;
            }
        }
        if (!_managedHosting)
        {
            var err = _ops.TryRun(
                ProjectId,
                $"Run workflow — {chain.Name}",
                $"/observability?tab=workflows&workflowRun={chainRunId}",
                async (sp, ct) =>
                {
                    var result = await sp.GetRequiredService<PlaceContextService>()
                        .RunJobChainAsync(chain.Id, payload, chainRunId, stepOverrides, ct);
                    return $"workflow finished — {result.Status}";
                },
                correlationKey: RunStatusWatchService.ChainRunKey(chainRunId)
            );
            if (err is not null)
            {
                Message = err;
                NotifyStateChanged();
                return;
            }
        }

        RunPromptChain = null;
        RunPromptSteps.Clear();
        _runPrompt.Clear();
        RunAttachment = "";
        PendingChainRunId = chainRunId;
        PendingChainId = chain.Id;
        Message = _managedHosting
            ? $"Run of {chain.Name} queued durably — it will appear here when a worker starts it."
            : $"Run of {chain.Name} started — follow it in the notifications bell.";

        // Open the editor and switch to the runs tab so the user sees the run appear live.
        EditChainId = chain.Id;
        EditorTab = "runs";
        EditorView = "runs";
        ShowEditor = true;
        _executionStepIndex = null;
        OpenRun = null;
        StopPolling();
        await RefreshRunsAsync(chain.Id, pendingChainRunId: chainRunId);
    }

    // ── Cancel ────────────────────────────────────────────────────────────────────────────────
    public async Task CancelChainRunAsync(Guid chainRunId)
    {
        try
        {
            await _svc.CancelChainRunAsync(chainRunId);
            Message = "Workflow run cancellation requested.";
            if (OpenRun?.Id == chainRunId && OpenRun is { } run)
            {
                await RefreshRunsAsync(run.ChainId);
            }
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        NotifyStateChanged();
    }

    public async Task CancelStepRunAsync(Guid runId)
    {
        try
        {
            await _svc.CancelJobRunAsync(runId);
            StepRunDetail = null;
            if (OpenRun is { } run)
            {
                await RefreshRunsAsync(run.ChainId);
            }
            Message = "Step run cancellation requested.";
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        NotifyStateChanged();
    }

    // ── Runs tab ──────────────────────────────────────────────────────────────────────────────
    public async Task SwitchToRunsTabAsync()
    {
        EditorTab = "runs";
        EditorView = "runs";
        StepRunDetail = null;
        if (EditChainId is { } chainId)
        {
            await RefreshRunsAsync(chainId);
        }
    }

    public async Task OpenChainRunAsync(ChainRunView run)
    {
        StopPolling();
        StepOutputError = null;
        ClearHumanConfirmation();
        _executionStepIndex = null;
        OpenRun = run;
        StepRunDetail = null;
        SelectedStepRunId = null;
        await RefreshStepOutputAsync(run);
        if (!IsTerminal(run.Status))
        {
            StartPolling(run.ChainId);
        }
        NotifyStateChanged();
    }

    public void CloseChainRun()
    {
        ClearHumanConfirmation();
        StopPolling();
        StepOutputError = null;
        _executionStepIndex = null;
        OpenRun = null;
        StepRunDetail = null;
        SelectedStepRunId = null;
        NotifyStateChanged();
    }

    public async Task OpenStepRunAsync(ChainStepRunView step)
    {
        StepOutputError = null;
        StepRunDetail = null;
        SelectedStepRunId = step.RunId;
        try
        {
            if (step.RunId is { } rid)
            {
                var detail = await _svc.GetJobRunAsync(rid);
                if (SelectedStepRunId != rid)
                {
                    return;
                }
                StepRunDetail = detail;
            }
        }
        catch
        {
            if (SelectedStepRunId == step.RunId)
            {
                StepRunDetail = null;
                StepOutputError = "Could not load this job's logs. Select the job again to retry.";
            }
        }
        NotifyStateChanged();
    }

    public IReadOnlyList<ChainStepRunView> LogSteps => OpenRun is null ? [] : OpenRun.Steps
        .Where(step => step.RunId.HasValue)
        .Concat(OpenRun.Steps.SelectMany(step => (step.AgentActivity ?? [])
            .Reverse().DistinctBy(activity => activity.RunId)
            .Where(activity => activity.RunId.HasValue)
            .Select(activity => new ChainStepRunView(step.Index, step.StageIndex, step.BranchIndex,
                activity.JobId ?? Guid.Empty, $"{step.JobName} · {activity.Tool}", activity.RunId,
                activity.Kind == "job_started" ? "Running" : activity.Kind == "tool_failed" ? "Failed" : "Completed",
                activity.At, activity.Kind == "job_started" ? null : activity.At))))
        .DistinctBy(step => step.RunId).ToArray();

    public Task OpenAgentJobRunAsync(Guid runId) => SelectLogStepAsync(runId.ToString());

    public Task SelectLogStepAsync(string? runId)
    {
        var step = Guid.TryParse(runId, out var id)
            ? LogSteps.FirstOrDefault(step => step.RunId == id) : null;
        if (step is not null)
        {
            return OpenStepRunAsync(step);
        }
        CloseStepRun();
        return Task.CompletedTask;
    }

    public void CloseStepRun()
    {
        StepOutputError = null;
        StepRunDetail = null;
        SelectedStepRunId = null;
        NotifyStateChanged();
    }

    private async Task RefreshRunsAsync(Guid chainId, Guid? pendingChainRunId = null)
    {
        var version = _pageVersion;
        await _refreshLock.WaitAsync();
        try
        {
            if (version != _pageVersion || EditChainId != chainId)
            {
                return;
            }
            var runs = await _svc.ListChainRunsAsync(chainId);
            if (version != _pageVersion || EditChainId != chainId)
            {
                return;
            }
            ChainRuns = runs;
            if (PendingHumanAnswer is not null && !HumanConfirmationCurrent)
            {
                ClearHumanConfirmation();
            }
            var targetId = pendingChainRunId ?? PendingChainRunId;
            async Task SelectRunAsync()
            {
                if (targetId is { } pending)
                {
                    async Task SelectPendingRunAsync()
                    {
                        if (ChainRuns.FirstOrDefault(r => r.Id == pending) is { } pendingRun)
                        {
                            OpenRun = pendingRun;
                            _pendingSubmissionId = null;
                            FollowNewest = true;
                            await RefreshStepOutputAsync(pendingRun);
                            if (IsTerminal(pendingRun.Status))
                            {
                                PendingChainRunId = null;
                                PendingChainId = null;
                            }
                            return;
                        }
                        if (_pendingSubmissionId is { } receiptId && _submissions is not null)
                        {
                            var receipt = await _submissions.GetAsync(receiptId);
                            if (receipt is null || receipt.Status is "Failed" or "Expired" or "Cancelled")
                            {
                                Message = receipt?.Error ?? "This queued workflow could not be started.";
                                PendingChainRunId = null;
                                PendingChainId = null;
                                _pendingSubmissionId = null;
                            }
                        }
                    }
                    await SelectPendingRunAsync();                    // Keep polling until a queued run appears or the submission fails.
                    if (PendingChainRunId is not null)
                    {
                        StartPolling(chainId);
                    }
                    return;
                }
                if (OpenRun is { } current)
                {
                    var updated = ChainRuns.FirstOrDefault(r => r.Id == current.Id);
                    if (updated is not null)
                    {
                        OpenRun = updated;
                        await RefreshStepOutputAsync(updated);
                        if (!IsTerminal(updated.Status))
                        {
                            StartPolling(chainId);
                        }
                    }
                }
            }
            await SelectRunAsync();
            // If the pending run is terminal and no longer needed, clear it.
            if (
                PendingChainRunId is { } stillPending
                && ChainRuns.FirstOrDefault(r => r.Id == stillPending) is { } sp
                && IsTerminal(sp.Status)
            )
            {
                PendingChainRunId = null;
                PendingChainId = null;
            }
        }
        catch { }
        finally
        {
            _refreshLock.Release();
        }
        NotifyStateChanged();
    }

    private async Task RefreshStepOutputAsync(ChainRunView run)
    {
        var logSteps = ExecutionStep is null ? LogSteps : ExecutionLogSteps;
        var runId = SelectedStepRunId;
        if (runId is null || logSteps.All(s => s.RunId != runId))
        {
            runId =
                logSteps.Where(s => s.RunId.HasValue && s.Status == "Running")
                    .OrderByDescending(s => s.StartedAt)
                    .Select(s => s.RunId)
                    .FirstOrDefault()
                ?? logSteps.Where(s =>
                        s.RunId.HasValue
                        && s.Status is "Succeeded" or "Partial" or "Failed" or "Cancelled" or "Completed"
                    )
                    .OrderByDescending(s => s.FinishedAt ?? s.StartedAt)
                    .Select(s => s.RunId)
                    .FirstOrDefault();
            SelectedStepRunId = runId;
        }

        if (runId is null)
        {
            StepRunDetail = null;
            return;
        }

        try
        {
            var detail = await _svc.GetJobRunAsync(runId.Value);
            if (SelectedStepRunId != runId || OpenRun?.Id != run.Id)
            {
                return;
            }
            StepRunDetail = detail;
            StepOutputError = null;
        }
        catch
        {
            // Keep the last good output visible through a transient refresh failure.
            if (SelectedStepRunId == runId)
            {
                StepOutputError = "Log refresh failed. Showing the last available output; retrying while the run is active.";
            }
        }
    }

    private void StartPolling(Guid chainId)
    {
        if (_pollCts is not null)
        {
            return;
        }
        _pollCts = new CancellationTokenSource();
        var ct = _pollCts.Token;
        _ = Task.Run(
            async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(1000, ct).ConfigureAwait(false);
                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }
                    try
                    {
                        await RefreshRunsAsync(chainId);
                        if (
                            PendingChainRunId is null
                            && OpenRun is { } open
                            && IsTerminal(open.Status)
                        )
                        {
                            break;
                        }
                    }
                    catch { }
                }
                if (_pollCts is not null && _pollCts.Token == ct)
                {
                    _pollCts.Dispose();
                    _pollCts = null;
                }
            },
            ct
        );
    }

    private static bool IsTerminal(string status) =>
        status is "Succeeded" or "Failed" or "Partial" or "Cancelled";

    private void StopPolling()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
    }
}
