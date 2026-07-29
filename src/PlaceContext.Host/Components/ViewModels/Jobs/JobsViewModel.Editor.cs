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
    // ── Editor state ──────────────────────────────────────────────────────────────────────────
    public bool ShowEditor { get; private set; }
    public bool Saving { get; private set; }
    public Guid? EditJobId { get; private set; }
    public string? EditorError { get; private set; }
    public string EditorTab { get; set; } = "details";
    public Guid? SelectedTriggerId { get; set; }

    public string EdName { get; set; } = "";
    public string EdDescription { get; set; } = "";
    public string EdMapSourceKind { get; set; } = "image";
    public string EdMapImage { get; set; } = "";
    public string EdMapRuntimeId { get; set; } = "node";
    public string EdMapEntrypoint { get; set; } = "";
    public string EdMapSource { get; set; } = "";
    public IReadOnlyList<CodeFileDto> EdMapFiles { get; set; } = Array.Empty<CodeFileDto>();
    public string EdInputPayloadsRaw { get; set; } = "{}";
    public string EdPayloadMode { get; set; } = "raw";
    public Dictionary<string, string> EdPayloadForm { get; set; } = new();
    public string EdMapEnvRaw { get; set; } = "";
    public int EdConcurrency { get; set; } = 1;
    public string EdSuccessCodesRaw { get; set; } = "0";
    public string EdPartialCodesRaw { get; set; } = "";
    public bool EdAllowNetworkEgress { get; set; }
    public int EdRetryCount { get; set; }
    public int EdRetryDelaySeconds { get; set; }
    public List<ParamEdit> EdParams { get; } = new();
    public List<PlaceContext.Domain.ValueObjects.PostJobActionKind> EdPostJobActions { get; } = new();
    public PlaceContext.Domain.ValueObjects.JobReturnType EdReturnType { get; set; } = PlaceContext.Domain.ValueObjects.JobReturnType.Json;
    public string EdReturnFileName { get; set; } = "";

    // ── Jobs ──────────────────────────────────────────────────────────────────────────────────
    public async Task OpenJobAsync(JobView job)
    {
        EditJob(job);
        SelectedJobId = job.Id;
        RunDetail = null;
        try
        {
            Runs = await _svc.ListJobRunsAsync(job.Id);
        }
        catch (Exception ex) { Message = ex.Message; }
        await LoadJobTelemetryAsync(job.Id);
        NotifyStateChanged();
    }

    public async Task SwitchToRunsTabAsync()
    {
        EditorTab = "runs";
        RunDetail = null;
        if (EditJobId is { } jobId)
        {
            try { Runs = await _svc.ListJobRunsAsync(jobId); } catch { }
            await LoadJobTelemetryAsync(jobId);
        }
        NotifyStateChanged();
    }

    private async Task LoadJobTelemetryAsync(Guid jobId)
    {
        try { JobTelemetry = await _svc.ListJobRunTelemetryAsync(jobId); }
        catch { JobTelemetry = null; }
    }

    // ── Editor ────────────────────────────────────────────────────────────────────────────────
    public void NewJob()
    {
        EditJobId = null;
        EdName = "";
        EdDescription = "";
        EdMapSourceKind = "image";
        EdMapImage = "";
        EdMapRuntimeId = "node";
        EdMapEntrypoint = "";
        EdMapSource = "";
        EdMapFiles = Array.Empty<CodeFileDto>();
        EdInputPayloadsRaw = "{}";
        EdMapEnvRaw = "";
        EdConcurrency = 1;
        EdSuccessCodesRaw = "0";
        EdPartialCodesRaw = "";
        EdAllowNetworkEgress = false;
        EdRetryCount = 0;
        EdRetryDelaySeconds = 0;
        EdParams.Clear();
        EdPostJobActions.Clear();
        EdReturnType = PlaceContext.Domain.ValueObjects.JobReturnType.Json;
        EdReturnFileName = "";
        EdMcpConnectionIds.Clear();
        EditorError = null;
        EditorTab = "details";
        ShowEditor = true;
        NotifyStateChanged();
    }

    public void EditJob(JobView job)
    {
        EditJobId = job.Id;
        EdName = job.Name;
        EdDescription = job.Description ?? "";
        EdMapSourceKind = job.MapSourceKind;
        EdMapImage = job.MapImage ?? "";
        EdMapRuntimeId = job.MapRuntimeId ?? "node";
        EdMapEntrypoint = job.MapEntrypoint ?? "";
        EdMapSource = job.MapSource ?? "";
        EdMapFiles = job.MapFiles;
        EdInputPayloadsRaw = string.Join("\n", job.InputPayloads);
        EdMapEnvRaw = string.Join("\n", job.MapEnv.Select(kv => $"{kv.Key}={kv.Value}"));
        EdConcurrency = job.ConcurrencyLimit;
        EdSuccessCodesRaw = string.Join(",", job.SuccessExitCodes);
        EdPartialCodesRaw = string.Join(",", job.PartialExitCodes);
        EdAllowNetworkEgress = job.AllowNetworkEgress;
        EdRetryCount = job.RetryCount;
        EdRetryDelaySeconds = job.RetryDelaySeconds;
        EdParams.Clear();
        EdParams.AddRange(job.Parameters.Select(ParamEdit.From));
        EdPostJobActions.Clear();
        EdPostJobActions.AddRange(job.PostJobActions);
        EdReturnType = job.ReturnType;
        EdReturnFileName = job.ReturnFileName ?? "";
        EdMcpConnectionIds.Clear();
        foreach (var id in job.McpConnectionIds)
            EdMcpConnectionIds.Add(id.ToString());
        EditorError = null;
        EditorTab = "details";
        SelectedTriggerId = null;
        ShowEditor = true;
        NotifyStateChanged();
    }

    public void CloseEditor()
    {
        ShowEditor = false;
        EditorError = null;
        RunDetail = null;
        SelectedTriggerId = null;
        EditorTab = "details";
        NotifyStateChanged();
    }

    public void SetEditorTab(string tab)
    {
        EditorTab = tab;
        if (tab == "triggers") SelectedTriggerId = null;
        NotifyStateChanged();
    }

    // ── Payload form ──────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<JobParameterDto> DeclaredParams()
        => EdParams.Where(p => !string.IsNullOrWhiteSpace(p.Name)).Select(p => p.ToDto()).ToList();

    public void SwitchPayloadMode(string mode)
    {
        if (mode == "form")
        {
            var first = EdInputPayloadsRaw
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            EdPayloadForm = JsonPayloadHelper.FlattenScalars(first);
        }
        else if (EdPayloadMode == "form")
        {
            ComposePayloadFromForm();
        }
        EdPayloadMode = mode;
        NotifyStateChanged();
    }

    public string PayloadFormValue(string name) => EdPayloadForm.GetValueOrDefault(name, "");

    public void SetPayloadFormValue(string name, string value)
    {
        EdPayloadForm[name] = value;
        ComposePayloadFromForm();
        NotifyStateChanged();
    }

    private void ComposePayloadFromForm()
        => EdInputPayloadsRaw = System.Text.Json.JsonSerializer.Serialize(
            DeclaredParams().ToDictionary(p => p.Name, p => (object)PayloadFormValue(p.Name)));

    public void TogglePostJobAction(PlaceContext.Domain.ValueObjects.PostJobActionKind kind, bool on)
    {
        if (on) { if (!EdPostJobActions.Contains(kind)) EdPostJobActions.Add(kind); }
        else EdPostJobActions.Remove(kind);
        NotifyStateChanged();
    }

    public async Task SaveJobAsync()
    {
        EditorError = null;
        if (string.IsNullOrWhiteSpace(EdName)) { EditorError = "Name is required."; NotifyStateChanged(); return; }

        var payloads = EdInputPayloadsRaw
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (payloads.Count == 0) { EditorError = "At least one input payload is required."; NotifyStateChanged(); return; }

        var env = new Dictionary<string, string>();
        foreach (var line in EdMapEnvRaw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = line.IndexOf('=');
            if (eq > 0)
                env[line[..eq].Trim()] = line[(eq + 1)..];
        }

        var successCodes = ParseInts(EdSuccessCodesRaw, out var sucErr);
        if (sucErr is not null) { EditorError = $"Success codes: {sucErr}"; NotifyStateChanged(); return; }
        var partialCodes = ParseInts(EdPartialCodesRaw, out var parErr);
        if (parErr is not null) { EditorError = $"Partial codes: {parErr}"; NotifyStateChanged(); return; }

        var parameters = DeclaredParams();

        Saving = true;
        NotifyStateChanged();
        try
        {
            if (EditJobId.HasValue)
            {
                IReadOnlyList<CodeFileDto>? mapFiles = null;
                if (EdMapSourceKind == "code" && EdMapFiles.Count > 1)
                {
                    var entry = !string.IsNullOrWhiteSpace(EdMapEntrypoint) ? EdMapEntrypoint : DefaultEntrypointFor(EdMapRuntimeId);
                    mapFiles = EdMapFiles.Select(f => f.Path == entry ? f with { Content = EdMapSource } : f).ToList();
                }

                var mcpIds = EdMcpConnectionIds.Select(id => Guid.Parse(id)).ToList();

                var cmd = new UpdateJobCommand(
                    JobId: EditJobId.Value,
                    Name: EdName.Trim(),
                    Description: string.IsNullOrWhiteSpace(EdDescription) ? null : EdDescription.Trim(),
                    MapImage: EdMapSourceKind == "image" ? EdMapImage : null,
                    MapRuntimeId: EdMapSourceKind == "code" ? EdMapRuntimeId : null,
                    MapSource: EdMapSourceKind == "code" && mapFiles is null ? EdMapSource : null,
                    MapFiles: mapFiles,
                    MapEntrypoint: EdMapSourceKind == "code" && !string.IsNullOrWhiteSpace(EdMapEntrypoint) ? EdMapEntrypoint : null,
                    InputPayloads: payloads,
                    MapEnv: env,
                    ReduceImage: null,
                    ReduceRuntimeId: null,
                    ReduceSource: null,
                    ReduceEntrypoint: null,
                    ReduceEnv: null,
                    ConcurrencyLimit: EdConcurrency,
                    SuccessExitCodes: successCodes,
                    PartialExitCodes: partialCodes,
                    AllowNetworkEgress: EdAllowNetworkEgress,
                    Parameters: parameters,
                    PostJobActions: EdPostJobActions.ToList(),
                    ReturnType: EdReturnType,
                    ReturnFileName: string.IsNullOrWhiteSpace(EdReturnFileName) ? null : EdReturnFileName.Trim(),
                    RetryCount: EdRetryCount,
                    RetryDelaySeconds: EdRetryDelaySeconds,
                    McpConnectionIds: mcpIds);

                await _svc.UpdateJobAsync(cmd);
                Message = $"Job '{EdName.Trim()}' updated.";
            }
            else
            {
                var mcpIds = EdMcpConnectionIds.Select(id => Guid.Parse(id)).ToList();

                var cmd = new CreateJobCommand(
                    ProjectId: ProjectId,
                    Name: EdName.Trim(),
                    Description: string.IsNullOrWhiteSpace(EdDescription) ? null : EdDescription.Trim(),
                    MapImage: EdMapSourceKind == "image" ? EdMapImage : null,
                    MapRuntimeId: EdMapSourceKind == "code" ? EdMapRuntimeId : null,
                    MapSource: EdMapSourceKind == "code" ? EdMapSource : null,
                    MapEntrypoint: EdMapSourceKind == "code" && !string.IsNullOrWhiteSpace(EdMapEntrypoint) ? EdMapEntrypoint : null,
                    InputPayloads: payloads,
                    MapEnv: env,
                    ReduceImage: null,
                    ReduceRuntimeId: null,
                    ReduceSource: null,
                    ReduceEntrypoint: null,
                    ReduceEnv: null,
                    ConcurrencyLimit: EdConcurrency,
                    SuccessExitCodes: successCodes,
                    PartialExitCodes: partialCodes,
                    AllowNetworkEgress: EdAllowNetworkEgress,
                    Parameters: parameters,
                    PostJobActions: EdPostJobActions.ToList(),
                    ReturnType: EdReturnType,
                    ReturnFileName: string.IsNullOrWhiteSpace(EdReturnFileName) ? null : EdReturnFileName.Trim(),
                    RetryCount: EdRetryCount,
                    RetryDelaySeconds: EdRetryDelaySeconds,
                    McpConnectionIds: mcpIds);

                await _svc.CreateJobAsync(cmd);
                Message = $"Job '{EdName.Trim()}' created.";
            }

            Jobs = await _svc.ListJobsAsync(ProjectId);
            ShowEditor = false;
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

    public async Task DeleteJobAsync(Guid jobId)
    {
        try
        {
            await _svc.DeleteJobAsync(jobId);
            Jobs = await _svc.ListJobsAsync(ProjectId);
            ConfirmDeleteId = null;
            if (EditJobId == jobId) CloseEditor();
            Message = "Job deleted.";
        }
        catch (Exception ex) { Message = ex.Message; }
        NotifyStateChanged();
    }
}
