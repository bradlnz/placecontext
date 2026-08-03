using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels.Helpers;
using PlaceContext.Infrastructure.Operations;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobChainsViewModel
{
    // ── Pipeline runs state ───────────────────────────────────────────────────────────────────
    public IReadOnlyList<ChainRunView>? ChainRuns { get; private set; }
    public ChainRunView? OpenRun { get; private set; }
    public JobRunDetailView? StepRunDetail { get; private set; }
    public Guid? SelectedStepRunId { get; private set; }
    public DateTimeOffset? LiveOutputUpdatedAt { get; private set; }
    public string? LiveOutputText { get; private set; }
    public bool FollowNewest { get; set; }
    private CancellationTokenSource? _pollCts;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    // ── Run-input prompt state ────────────────────────────────────────────────────────────────
    public JobChainView? RunPromptChain { get; private set; }
    public List<(int Index, JobView Job)> RunPromptSteps { get; } = new();
    public Dictionary<string, string> RunArgs => _runPrompt.Args;
    public string? RunPromptError => _runPrompt.Error;

    // ── Run chain ─────────────────────────────────────────────────────────────────────────────
    public async Task RunChainAsync(JobChainView chain)
    {
        var plan = ChainParameterPromptPlan.Build(chain, Jobs ?? Array.Empty<JobView>());
        if (plan.Steps.Count > 0)
        {
            RunPromptChain = chain;
            RunPromptSteps.Clear();
            RunPromptSteps.AddRange(plan.Steps);
            _runPrompt.Reset(plan.Defaults);
            NotifyStateChanged();
            return;
        }
        await RunChainCoreAsync(chain, null);
    }

    public string GetArg(string name) => _runPrompt.Get(name);
    public void SetArg(string name, string value) => _runPrompt.Set(name, value);

    public async Task SubmitRunPromptAsync()
    {
        if (RunPromptChain is null) return;

        if (!_runPrompt.ValidateChainStepParameters(RunPromptSteps))
        {
            NotifyStateChanged();
            return;
        }

        // Form keys stay stepN:param; wire format is per-step bare-param JSON via StepPayloadOverrides.
        var overrides = _runPrompt.ToStepPayloadOverrides(RunPromptSteps);
        var payload = overrides.GetValueOrDefault(0);
        await RunChainCoreAsync(RunPromptChain, payload, overrides);
    }

    public void CancelRunPrompt()
    {
        RunPromptChain = null;
        RunPromptSteps.Clear();
        _runPrompt.Clear();
        NotifyStateChanged();
    }

    private async Task RunChainCoreAsync(JobChainView chain, string? payload, IReadOnlyDictionary<int, string>? stepOverrides = null)
    {
        Message = null;
        var chainRunId = Guid.NewGuid();
        var err = _ops.TryRun(ProjectId, $"Run chain — {chain.Name}", $"/project/{ProjectId}/chains",
            async (sp, ct) =>
            {
                var result = await sp.GetRequiredService<IPlaceContextService>()
                    .RunJobChainAsync(chain.Id, payload, chainRunId, stepOverrides, ct);
                return $"chain finished — {result.Status}";
            },
            correlationKey: RunStatusWatchService.ChainRunKey(chainRunId));
        if (err is not null) { Message = err; NotifyStateChanged(); return; }

        RunPromptChain = null;
        RunPromptSteps.Clear();
        _runPrompt.Clear();
        PendingChainRunId = chainRunId;
        PendingChainId = chain.Id;
        Message = $"Run of {chain.Name} started — follow it in the notifications bell.";

        // Open the editor and switch to the runs tab so the user sees the run appear live.
        EditChainId = chain.Id;
        EditorTab = "runs";
        ShowEditor = true;
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
            Message = "Chain run cancellation requested.";
            if (OpenRun?.Id == chainRunId && OpenRun is { } run)
                await RefreshRunsAsync(run.ChainId);
        }
        catch (Exception ex) { Message = ex.Message; }
        NotifyStateChanged();
    }

    public async Task CancelStepRunAsync(Guid runId)
    {
        try
        {
            await _svc.CancelJobRunAsync(runId);
            StepRunDetail = null;
            if (OpenRun is { } run)
                await RefreshRunsAsync(run.ChainId);
            Message = "Step run cancellation requested.";
        }
        catch (Exception ex) { Message = ex.Message; }
        NotifyStateChanged();
    }

    // ── Runs tab ──────────────────────────────────────────────────────────────────────────────
    public async Task SwitchToRunsTabAsync()
    {
        EditorTab = "runs";
        StepRunDetail = null;
        if (EditChainId is { } chainId)
            await RefreshRunsAsync(chainId);
    }

    public async Task OpenChainRunAsync(ChainRunView run)
    {
        OpenRun = run;
        StepRunDetail = null;
        SelectedStepRunId = null;
        LiveOutputText = null;
        await RefreshStepOutputAsync(run);
        if (!IsTerminal(run.Status))
            StartPolling(run.ChainId);
        NotifyStateChanged();
    }

    public void CloseChainRun()
    {
        OpenRun = null;
        StepRunDetail = null;
        SelectedStepRunId = null;
        LiveOutputText = null;
        NotifyStateChanged();
    }

    public async Task OpenStepRunAsync(ChainStepRunView step)
    {
        try
        {
            SelectedStepRunId = step.RunId;
            if (step.RunId is { } rid)
            {
                StepRunDetail = await _svc.GetJobRunAsync(rid);
                UpdateLiveOutput(rid);
                LiveOutputUpdatedAt = DateTimeOffset.UtcNow;
            }
            else StepRunDetail = null;
        }
        catch { StepRunDetail = null; }
        NotifyStateChanged();
    }

    public void CloseStepRun()
    {
        StepRunDetail = null;
        SelectedStepRunId = null;
        LiveOutputText = null;
        NotifyStateChanged();
    }

    private async Task RefreshRunsAsync(Guid chainId, Guid? pendingChainRunId = null)
    {
        if (!await _refreshLock.WaitAsync(0)) return; // skip if a refresh is already in-flight
        try
        {
            ChainRuns = await _svc.ListChainRunsAsync(chainId);

            var targetId = pendingChainRunId ?? PendingChainRunId;
            if (targetId is { } pending)
            {
                // If the pending run has appeared, open it and start/continue polling.
                if (ChainRuns.FirstOrDefault(r => r.Id == pending) is { } pendingRun)
                {
                    OpenRun = pendingRun;
                    FollowNewest = true;
                    await RefreshStepOutputAsync(pendingRun);
                    if (IsTerminal(pendingRun.Status))
                    {
                        PendingChainRunId = null;
                        PendingChainId = null;
                    }
                }
                // Keep polling until the pending run is found and terminal.
                StartPolling(chainId);
            }
            else if (OpenRun is { } current)
            {
                var updated = ChainRuns.FirstOrDefault(r => r.Id == current.Id);
                if (updated is not null)
                {
                    OpenRun = updated;
                    await RefreshStepOutputAsync(updated);
                    if (!IsTerminal(updated.Status))
                        StartPolling(chainId);
                }
            }

            // If the pending run is terminal and no longer needed, clear it.
            if (PendingChainRunId is { } stillPending
                && ChainRuns.FirstOrDefault(r => r.Id == stillPending) is { } sp
                && IsTerminal(sp.Status))
            {
                PendingChainRunId = null;
                PendingChainId = null;
            }
        }
        catch { }
        finally { _refreshLock.Release(); }
        NotifyStateChanged();
    }

    private async Task RefreshStepOutputAsync(ChainRunView run)
    {
        var runId = SelectedStepRunId;
        if (runId is null || run.Steps.All(s => s.RunId != runId))
        {
            runId = run.Steps
                .Where(s => s.RunId.HasValue && s.Status == "Running")
                .OrderByDescending(s => s.StartedAt)
                .Select(s => s.RunId)
                .FirstOrDefault()
                ?? run.Steps
                    .Where(s => s.RunId.HasValue && s.Status is "Succeeded" or "Partial" or "Failed" or "Cancelled")
                    .OrderByDescending(s => s.FinishedAt ?? s.StartedAt)
                    .Select(s => s.RunId)
                    .FirstOrDefault();
            SelectedStepRunId = runId;
        }

        if (runId is null)
        {
            StepRunDetail = null;
            LiveOutputText = null;
            return;
        }

        try
        {
            StepRunDetail = await _svc.GetJobRunAsync(runId.Value);
            UpdateLiveOutput(runId.Value);
        }
        catch
        {
            // Keep the last good output visible through a transient refresh failure.
        }
    }

    private void UpdateLiveOutput(Guid runId)
    {
        var live = _outputBuffer.Snapshot(runId);
        LiveOutputText = live?.Text;
        LiveOutputUpdatedAt = live?.UpdatedAt ?? DateTimeOffset.UtcNow;
    }

    private void StartPolling(Guid chainId)
    {
        if (_pollCts is not null) return;
        _pollCts = new CancellationTokenSource();
        var ct = _pollCts.Token;
        var started = DateTimeOffset.UtcNow;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(1000, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested) break;

                // Stop polling after a generous timeout.
                if (DateTimeOffset.UtcNow - started > TimeSpan.FromMinutes(5))
                {
                    PendingChainRunId = null;
                    PendingChainId = null;
                    NotifyStateChanged();
                    break;
                }

                try
                {
                    await RefreshRunsAsync(chainId);
                    if (PendingChainRunId is null && OpenRun is { } open && IsTerminal(open.Status))
                        break;
                }
                catch { }
            }
            if (_pollCts is not null && _pollCts.Token == ct)
            {
                _pollCts.Dispose();
                _pollCts = null;
            }
        }, ct);
    }

    private static bool IsTerminal(string status)
        => status is "Succeeded" or "Failed" or "Partial" or "Cancelled";

    private void StopPolling()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
    }

}
