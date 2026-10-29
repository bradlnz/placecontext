using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components.Forms;
using System.Text.Json;
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
    public bool RunFileMode { get; private set; }
    public bool RunFileLoading { get; private set; }
    public string? RunFileName { get; private set; }
    private string? _runFilePayload;

    public void UseRunFields() { RunFileMode = false; _runPrompt.SetError(null); }
    public void UseRunFile() { RunFileMode = true; _runPrompt.SetError(null); }

    public async Task ReadRunFileAsync(InputFileChangeEventArgs args)
    {
        if (RunPromptJob is not { } job || RunFileLoading) return;
        RunFileLoading = true;
        _runFilePayload = null;
        RunFileName = null;
        _runPrompt.SetError(null);
        try
        {
            await using var stream = args.File.OpenReadStream(1_048_576);
            using var reader = new StreamReader(stream);
            var payload = ValidateRunFile(await reader.ReadToEndAsync(), job.Parameters);
            if (RunPromptJob?.Id != job.Id) return;
            _runFilePayload = payload;
            RunFileName = args.File.Name;
        }
        catch (Exception ex) { if (RunPromptJob?.Id == job.Id) _runPrompt.SetError(ex.Message); }
        finally { RunFileLoading = false; NotifyStateChanged(); }
    }

    public static string ValidateRunFile(string json, IReadOnlyList<JobParameterDto> parameters)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 1_048_576) throw new ArgumentException("Choose a JSON file no larger than 1 MiB.");
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("The file must contain a JSON object.");
        var missing = parameters.Where(parameter => parameter.Required &&
            (!document.RootElement.TryGetProperty(parameter.Name, out var value) || value.ValueKind == JsonValueKind.Null
                || value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString())))
            .Select(parameter => parameter.Label ?? parameter.Name).ToArray();
        if (missing.Length > 0) throw new ArgumentException("Required: " + string.Join(", ", missing));
        return document.RootElement.GetRawText();
    }

    // ── Runs ──────────────────────────────────────────────────────────────────────────────────
    public void OnOpsChanged() => _ = RefreshRunsAsync();

    private bool _refreshingRuns;

    private async Task RefreshRunsAsync()
    {
        if (_refreshingRuns || SelectedJobId is not { } jobId)
            return;
        _refreshingRuns = true;
        var version = _paneVersion;
        try
        {
            var runs = await _svc.ListJobRunsAsync(jobId);
            if (version != _paneVersion || SelectedJobId != jobId) return;
            if (
                RunDetail is { } open
                && runs.FirstOrDefault(r => r.Id == open.Id) is { } summary
                && summary.Status != open.Status
            )
            {
                var detail = await _svc.GetJobRunAsync(open.Id);
                var artifacts = await _svc.ListRunArtifactsAsync(open.Id);
                if (version != _paneVersion || RunDetail?.Id != open.Id) return;
                RunDetail = detail;
                RunArtifacts = artifacts;
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
        if (job is not null)
        {
            RunPromptJob = job;
            RunFileMode = false;
            _runFilePayload = null;
            RunFileName = null;
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
        _runFilePayload = null;
        RunFileName = null;
        _runPrompt.Clear();
        RunningJobId = null;
        // Open the editor/runs view so the user sees the run appear live.
        if (Jobs?.FirstOrDefault(job => job.Id == jobId) is { } selected) EditJob(selected);
        else InvalidateRunRequests();
        PendingRunId = runId;
        PendingRunJobId = jobId;
        var version = _paneVersion;
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
        if (version == _paneVersion && ShowEditor) StartRunPolling(jobId, runId);
    }

    private void StartRunPolling(Guid jobId, Guid runId)
    {
        StopRunPolling();
        _runPollCts = new CancellationTokenSource();
        var ct = _runPollCts.Token;
        var version = _paneVersion;
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
                            var detail = await _svc.GetJobRunAsync(runId);
                            var artifacts = await _svc.ListRunArtifactsAsync(runId);
                            if (ct.IsCancellationRequested || version != _paneVersion || SelectedJobId != jobId) break;
                            Runs = runs;
                            RunDetail = detail;
                            RunArtifacts = artifacts;
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
        if (RunFileLoading) return;
        if (RunFileMode)
        {
            if (_runFilePayload is null) { _runPrompt.SetError("Choose a valid JSON input file before running."); NotifyStateChanged(); return; }
            await RunJobCoreAsync(RunPromptJob.Id, _runFilePayload);
            return;
        }
        if (!_runPrompt.ValidateJobParameters(RunPromptJob.Parameters))
        {
            NotifyStateChanged();
            return;
        }
        await RunJobCoreAsync(RunPromptJob.Id, RunPromptJob.Parameters.Count == 0 ? null : _runPrompt.ToJobPayload(RunPromptJob.Parameters));
    }

    public void CancelRunPrompt()
    {
        RunPromptJob = null;
        _runFilePayload = null;
        RunFileName = null;
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
            {
                var version = _paneVersion;
                var detail = await _svc.GetJobRunAsync(runId);
                if (version == _paneVersion && RunDetail?.Id == runId) RunDetail = detail;
            }
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
        InvalidateRunRequests();
        var version = _paneVersion;
        try
        {
            var detail = await _svc.GetJobRunAsync(runId);
            var artifacts = await _svc.ListRunArtifactsAsync(runId);
            if (version != _paneVersion) return;
            RunDetail = detail;
            RunArtifacts = artifacts;

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
        InvalidateRunRequests();
        RunDetail = null;
        RunArtifacts = null;
        NotifyStateChanged();
    }
}
