using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels.Helpers;
using PlaceContext.Infrastructure.Operations;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobsViewModel
{
    // ── Run-input prompt state ────────────────────────────────────────────────────────────────
    public JobView? RunPromptJob { get; private set; }
    public Dictionary<string, string> RunArgs => _runPrompt.Args;
    public string? RunPromptError => _runPrompt.Error;

    // ── Runs ──────────────────────────────────────────────────────────────────────────────────
    public void OnOpsChanged() => _ = RefreshRunsAsync();

    private bool _refreshingRuns;

    private async Task RefreshRunsAsync()
    {
        if (_refreshingRuns || SelectedJobId is not { } jobId) return;
        _refreshingRuns = true;
        try
        {
            var runs = await _svc.ListJobRunsAsync(jobId);
            if (RunDetail is { } open
                && runs.FirstOrDefault(r => r.Id == open.Id) is { } summary
                && summary.Status != open.Status)
            {
                RunDetail = await _svc.GetJobRunAsync(open.Id);
                RunArtifacts = await _svc.ListRunArtifactsAsync(open.Id);
            }
            Runs = runs;
            NotifyStateChanged();
        }
        catch { }
        finally { _refreshingRuns = false; }
    }

    public async Task RunJobAsync(Guid jobId)
    {
        var job = Jobs?.FirstOrDefault(j => j.Id == jobId);
        if (job is not null && job.Parameters.Count > 0)
        {
            RunPromptJob = job;
            var stored = JsonPayloadHelper.FlattenScalars(job.InputPayloads);
            _runPrompt.Reset(job.Parameters.ToDictionary(
                p => p.Name,
                p => stored.GetValueOrDefault(p.Name, ""),
                StringComparer.Ordinal));
            NotifyStateChanged();
            return;
        }
        await RunJobCoreAsync(jobId, null);
    }

    private async Task RunJobCoreAsync(Guid jobId, string? payload)
    {
        Message = null;
        RunningJobId = jobId;
        var jobName = Jobs?.FirstOrDefault(j => j.Id == jobId)?.Name ?? "job";
        var runId = Guid.NewGuid();
        var err = _ops.TryRun(ProjectId, $"Run job — {jobName}", $"/observability?run={runId}",
            async (sp, ct) =>
            {
                var result = await sp.GetRequiredService<IPlaceContextService>().RunJobAsync(jobId, payload, runId, ct);
                return $"run finished — {result.Status}";
            },
            correlationKey: RunStatusWatchService.JobRunKey(runId));
        if (err is not null)
        {
            Message = err;
            RunningJobId = null;
            NotifyStateChanged();
            return;
        }
        RunPromptJob = null;
        _runPrompt.Clear();
        RunningJobId = null;
        Message = $"Run of {jobName} started in the background — follow it in the notifications bell.";
        if (SelectedJobId == jobId)
        {
            try { Runs = await _svc.ListJobRunsAsync(jobId); } catch { }
        }
        NotifyStateChanged();
    }

    public string GetArg(string name) => _runPrompt.Get(name);
    public void SetArg(string name, string value) => _runPrompt.Set(name, value);

    public async Task SubmitRunPromptAsync()
    {
        if (RunPromptJob is null) return;
        if (!_runPrompt.ValidateJobParameters(RunPromptJob.Parameters))
        {
            NotifyStateChanged();
            return;
        }
        await RunJobCoreAsync(RunPromptJob.Id, _runPrompt.ToJobPayload(RunPromptJob.Parameters));
    }

    public void CancelRunPrompt()
    {
        RunPromptJob = null;
        _runPrompt.Clear();
        NotifyStateChanged();
    }

    public async Task OpenRunDetailAsync(Guid runId)
    {
        try
        {
            RunDetail = await _svc.GetJobRunAsync(runId);
            RunArtifacts = await _svc.ListRunArtifactsAsync(runId);
        }
        catch (Exception ex) { Message = ex.Message; }
        NotifyStateChanged();
    }

    public async Task OpenRunDetailFromTriggerAsync(Guid runId)
    {
        EditorTab = "runs";
        await OpenRunDetailAsync(runId);
    }

    public void CloseRunDetail()
    {
        RunDetail = null;
        NotifyStateChanged();
    }

}
