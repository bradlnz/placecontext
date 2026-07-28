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
        // Only steps with declared parameters appear in the prompt (UI keys: stepN:param).
        var paramSteps = chain.Steps
            .Select((step, i) => (Index: i, Job: Jobs?.FirstOrDefault(j => j.Id == step.JobId)))
            .Where(x => x.Job is { Parameters.Count: > 0 })
            .Select(x => (x.Index, x.Job!))
            .ToList();

        if (paramSteps.Count > 0)
        {
            RunPromptChain = chain;
            RunPromptSteps.Clear();
            RunPromptSteps.AddRange(paramSteps);
            var defaults = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (idx, job) in RunPromptSteps)
            {
                var stored = JsonPayloadHelper.FlattenScalars(job.InputPayloads);
                foreach (var p in job.Parameters)
                    defaults[ArgKey(idx, p.Name)] = stored.GetValueOrDefault(p.Name, "");
            }
            _runPrompt.Reset(defaults);
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
        if (!await _refreshLock.WaitAsync(0)) return; // skip if a refresh is already in-flight
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
        finally { _refreshLock.Release(); }
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
                await Task.Delay(5000, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested) break;
                try
                {
                    if (OpenRun is { } run)
                    {
                        await RefreshRunsAsync(run.ChainId, openNewest: false);
                        // Stop polling once the run reaches a terminal state
                        if (OpenRun is { Status: "Succeeded" or "Failed" or "Partial" })
                            break;
                    }
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

}
