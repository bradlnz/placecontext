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
        if (_refreshingRuns || SelectedJobId is not { } jobId)
            return;
        _refreshingRuns = true;
        try
        {
            var runs = await _svc.ListJobRunsAsync(jobId);
            if (
                RunDetail is { } open
                && runs.FirstOrDefault(r => r.Id == open.Id) is { } summary
                && summary.Status != open.Status
            )
            {
                RunDetail = await _svc.GetJobRunAsync(open.Id);
                RunArtifacts = await _svc.ListRunArtifactsAsync(open.Id);
            }
            Runs = runs;

            // Clear pending state once the launched run reaches a terminal status.
            if (
                PendingRunId is { } pending
                && runs.FirstOrDefault(r => r.Id == pending) is { } p
                && p.Status is "Succeeded" or "Failed" or "Partial"
            )
            {
                PendingRunId = null;
                PendingRunJobId = null;
            }

            NotifyStateChanged();
        }
        catch { }
        finally
        {
            _refreshingRuns = false;
        }
    }

    public async Task RunJobAsync(Guid jobId)
    {
        var job = Jobs?.FirstOrDefault(j => j.Id == jobId);
        if (job is not null && job.Parameters.Count > 0)
        {
            RunPromptJob = job;
            var stored = JsonPayloadHelper.FlattenScalars(job.InputPayloads);
            _runPrompt.Reset(
                job.Parameters.ToDictionary(
                    p => p.Name,
                    p => stored.GetValueOrDefault(p.Name, ""),
                    StringComparer.Ordinal
                )
            );
            NotifyStateChanged();
            return;
        }
        await RunJobCoreAsync(jobId, null);
    }

    private async Task RunJobCoreAsync(Guid jobId, string? payload)
    {
        Message = null;
        var jobName = Jobs?.FirstOrDefault(j => j.Id == jobId)?.Name ?? "job";
        var runId = Guid.NewGuid();
        var err = _ops.TryRun(
            ProjectId,
            $"Run job — {jobName}",
            $"/observability?run={runId}",
            async (sp, ct) =>
            {
                var result = await sp.GetRequiredService<PlaceContextService>()
                    .RunJobAsync(jobId, payload, runId, ct);
                return $"run finished — {result.Status}";
            },
            correlationKey: RunStatusWatchService.JobRunKey(runId)
        );
        if (err is not null)
        {
            Message = err;
            NotifyStateChanged();
            return;
        }

        RunPromptJob = null;
        _runPrompt.Clear();
        RunningJobId = null;
        PendingRunId = runId;
        PendingRunJobId = jobId;

        // Open the editor/runs view so the user sees the run appear live.
        SelectedJobId = jobId;
        EditorTab = "runs";
        ShowEditor = true;
        RunDetail = null;
        try
        {
            Runs = await _svc.ListJobRunsAsync(jobId);
        }
        catch { }
        NotifyStateChanged();

        Message =
            $"Run of {jobName} started in the background — follow it in the notifications bell.";
        StartRunPolling(jobId, runId);
    }

    private void StartRunPolling(Guid jobId, Guid runId)
    {
        StopRunPolling();
        _runPollCts = new CancellationTokenSource();
        var ct = _runPollCts.Token;
        var started = DateTimeOffset.UtcNow;
        _ = Task.Run(
            async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(2000, ct).ConfigureAwait(false);
                    if (ct.IsCancellationRequested)
                        break;

                    // Stop polling after a generous timeout even if the run never appeared.
                    if (DateTimeOffset.UtcNow - started > TimeSpan.FromMinutes(2))
                    {
                        PendingRunId = null;
                        PendingRunJobId = null;
                        NotifyStateChanged();
                        break;
                    }

                    try
                    {
                        var runs = await _svc.ListJobRunsAsync(jobId);
                        if (ct.IsCancellationRequested)
                            break;

                        var found = runs.FirstOrDefault(r => r.Id == runId);
                        if (found is not null)
                        {
                            Runs = runs;
                            RunDetail = await _svc.GetJobRunAsync(runId);
                            RunArtifacts = await _svc.ListRunArtifactsAsync(runId);
                            NotifyStateChanged();
                            if (found.Status is "Succeeded" or "Failed" or "Partial")
                            {
                                PendingRunId = null;
                                PendingRunJobId = null;
                                NotifyStateChanged();
                                break;
                            }
                        }
                    }
                    catch { }
                }
            },
            ct
        );
    }

    private void StopRunPolling()
    {
        _runPollCts?.Cancel();
        _runPollCts?.Dispose();
        _runPollCts = null;
    }

    public void ClearPendingRun()
    {
        PendingRunId = null;
        PendingRunJobId = null;
        StopRunPolling();
    }

    public string GetArg(string name) => _runPrompt.Get(name);

    public void SetArg(string name, string value) => _runPrompt.Set(name, value);

    public async Task SubmitRunPromptAsync()
    {
        if (RunPromptJob is null)
            return;
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

    public async Task CancelRunAsync(Guid runId)
    {
        try
        {
            await _svc.CancelJobRunAsync(runId);
            Message = "Run cancellation requested.";
            if (RunDetail?.Id == runId)
                RunDetail = await _svc.GetJobRunAsync(runId);
            StopRunDetailPolling();
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        NotifyStateChanged();
    }

    public async Task OpenRunDetailAsync(Guid runId)
    {
        try
        {
            RunDetail = await _svc.GetJobRunAsync(runId);
            RunArtifacts = await _svc.ListRunArtifactsAsync(runId);

            if (RunDetail?.Status is "Queued" or "Running")
                StartRunDetailPolling(runId);
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
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
        StopRunDetailPolling();
        NotifyStateChanged();
    }
}
