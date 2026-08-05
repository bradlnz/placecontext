using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
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

    // ── Source editor (Monaco) ────────────────────────────────────────────────────────────────
    public const string SourceEditorId = "pcjobs-source-editor";
    public bool SourceMonaco { get; private set; } = true;
    public bool SourceMonacoReady { get; private set; }
    public string SourceEditorLanguage => EditorLanguageCatalog.ForPath(CurrentEntrypoint);

    public void ResetSourceEditor()
    {
        SourceMonaco = true;
        SourceMonacoReady = false;
    }

    public async Task AfterRenderAsync()
    {
        if (
            ShowEditor
            && EditJobId is null
            && EdMapSourceKind == "code"
            && SourceMonaco
            && !SourceMonacoReady
        )
        {
            SourceMonacoReady = true;
            try
            {
                if (
                    !await _js.InvokeAsync<bool>(
                        "pcmonaco.init",
                        SourceEditorId,
                        EdMapSource,
                        SourceEditorLanguage,
                        "vs-dark"
                    )
                )
                    SourceMonaco = false;
            }
            catch
            {
                SourceMonaco = false;
            }
            if (!SourceMonaco)
                NotifyStateChanged();
        }
    }

    public async Task OnSourceRuntimeChangedAsync()
    {
        if (SourceMonaco && SourceMonacoReady)
        {
            try
            {
                var current = await _js.InvokeAsync<string>("pcmonaco.getValue", SourceEditorId);
                if (current is not null)
                    EdMapSource = current;
                await _js.InvokeVoidAsync(
                    "pcmonaco.setValue",
                    SourceEditorId,
                    EdMapSource,
                    SourceEditorLanguage
                );
            }
            catch
            {
                SourceMonaco = false;
            }
        }
        NotifyStateChanged();
    }

    private async Task SyncSourceEditorAsync()
    {
        if (!SourceMonaco || !SourceMonacoReady)
            return;
        try
        {
            var value = await _js.InvokeAsync<string>("pcmonaco.getValue", SourceEditorId);
            if (value is not null)
                EdMapSource = value;
        }
        catch
        {
            SourceMonaco = false;
        }
    }

    // ── Template modal state ──────────────────────────────────────────────────────────────────
    public bool ShowTemplateModal { get; private set; }
    public JobTemplate? SelectedTemplate { get; private set; }
    public string TemplateFilter { get; set; } = "";
    public IReadOnlyList<JobTemplate> FilteredTemplates =>
        string.IsNullOrWhiteSpace(TemplateFilter)
            ? JobTemplateCatalog.All
            : JobTemplateCatalog.All
                .Where(t => t.Name.Contains(TemplateFilter, StringComparison.OrdinalIgnoreCase)
                         || t.Description.Contains(TemplateFilter, StringComparison.OrdinalIgnoreCase)
                         || t.Category.Contains(TemplateFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();

    public bool HasTemplateCredential(JobCredentialRequirement credential) =>
        VaultSecrets?.Any(s => s.Name.Equals(credential.EnvVarName, StringComparison.OrdinalIgnoreCase)) ?? false;

    public int MissingCredentialCount(JobTemplate template) =>
        template.RequiredCredentials.Count(c => !HasTemplateCredential(c));

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
    public bool EdAllowApiInvocation { get; set; }
    public int EdRetryCount { get; set; }
    public int EdRetryDelaySeconds { get; set; }
    public List<ParamEdit> EdParams { get; } = new();
    public List<PlaceContext.Domain.ValueObjects.PostJobActionKind> EdPostJobActions { get; } =
        new();
    public PlaceContext.Domain.ValueObjects.JobReturnType EdReturnType { get; set; } =
        PlaceContext.Domain.ValueObjects.JobReturnType.Json;
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
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        await LoadJobTelemetryAsync(job.Id);
        NotifyStateChanged();
    }

    public async Task SwitchToRunsTabAsync()
    {
        EditorTab = "runs";
        RunDetail = null;
        if (EditJobId is { } jobId)
        {
            try
            {
                Runs = await _svc.ListJobRunsAsync(jobId);
            }
            catch { }
            await LoadJobTelemetryAsync(jobId);
        }
        NotifyStateChanged();
    }

    private async Task LoadJobTelemetryAsync(Guid jobId)
    {
        try
        {
            JobTelemetry = await _svc.ListJobRunTelemetryAsync(jobId);
        }
        catch
        {
            JobTelemetry = null;
        }
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
        EdAllowApiInvocation = false;
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
        ResetSourceEditor();
        NotifyStateChanged();
    }

    public void OpenTemplateModal()
    {
        ShowTemplateModal = true;
        SelectedTemplate = null;
        TemplateFilter = "";
        NotifyStateChanged();
    }

    public void CloseTemplateModal()
    {
        ShowTemplateModal = false;
        SelectedTemplate = null;
        TemplateFilter = "";
        NotifyStateChanged();
    }

    public void SelectTemplate(JobTemplate template)
    {
        SelectedTemplate = template;
        NotifyStateChanged();
    }

    public void ApplySelectedTemplate()
    {
        if (SelectedTemplate is not { } template)
            return;

        NewJob();
        EdName = template.Name;
        EdDescription = template.Description;
        EdMapSourceKind = template.MapSourceKind;
        EdMapImage = template.MapImage;
        EdMapRuntimeId = template.MapRuntimeId ?? "node";
        EdMapEntrypoint = template.MapEntrypoint ?? "";
        EdMapSource = template.MapSource;
        EdMapEnvRaw = template.MapEnvRaw;
        EdInputPayloadsRaw = template.InputPayloadsRaw;
        EdReturnType = template.ReturnType;
        EdAllowNetworkEgress = template.AllowNetworkEgress;
        EdAllowApiInvocation = false;
        EdParams.Clear();
        EdParams.AddRange(template.Parameters.Select(p => new ParamEdit
        {
            Name = p.Name,
            Label = p.Label ?? "",
            Type = p.Type,
            Required = p.Required,
            OptionsRaw = p.Options is { Count: > 0 } ? string.Join(",", p.Options) : ""
        }));
        ShowTemplateModal = false;
        SelectedTemplate = null;
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
        EdAllowApiInvocation = job.AllowApiInvocation;
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
        ResetSourceEditor();
        NotifyStateChanged();
    }

    public void CloseEditor()
    {
        ShowEditor = false;
        EditorError = null;
        RunDetail = null;
        SelectedTriggerId = null;
        EditorTab = "details";
        ResetSourceEditor();
        NotifyStateChanged();
    }

    public void SetEditorTab(string tab)
    {
        EditorTab = tab;
        if (tab == "triggers")
            SelectedTriggerId = null;
        NotifyStateChanged();
    }

    // ── Payload form ──────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<JobParameterDto> DeclaredParams() =>
        EdParams.Where(p => !string.IsNullOrWhiteSpace(p.Name)).Select(p => p.ToDto()).ToList();

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

    private void ComposePayloadFromForm() =>
        EdInputPayloadsRaw = ParameterPromptState.ToJsonPayload(DeclaredParams(), PayloadFormValue);

    public void TogglePostJobAction(
        PlaceContext.Domain.ValueObjects.PostJobActionKind kind,
        bool on
    )
    {
        if (on)
        {
            if (!EdPostJobActions.Contains(kind))
                EdPostJobActions.Add(kind);
        }
        else
            EdPostJobActions.Remove(kind);
        NotifyStateChanged();
    }

    public async Task SaveJobAsync()
    {
        EditorError = null;
        await SyncSourceEditorAsync();
        if (string.IsNullOrWhiteSpace(EdName))
        {
            EditorError = "Name is required.";
            NotifyStateChanged();
            return;
        }

        var payloads = EdInputPayloadsRaw
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (payloads.Count == 0)
        {
            EditorError = "At least one input payload is required.";
            NotifyStateChanged();
            return;
        }

        var env = new Dictionary<string, string>();
        foreach (
            var line in EdMapEnvRaw.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        {
            var eq = line.IndexOf('=');
            if (eq > 0)
                env[line[..eq].Trim()] = line[(eq + 1)..];
        }

        var successCodes = ParseInts(EdSuccessCodesRaw, out var sucErr);
        if (sucErr is not null)
        {
            EditorError = $"Success codes: {sucErr}";
            NotifyStateChanged();
            return;
        }
        var partialCodes = ParseInts(EdPartialCodesRaw, out var parErr);
        if (parErr is not null)
        {
            EditorError = $"Partial codes: {parErr}";
            NotifyStateChanged();
            return;
        }

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
                    var entry = !string.IsNullOrWhiteSpace(EdMapEntrypoint)
                        ? EdMapEntrypoint
                        : DefaultEntrypointFor(EdMapRuntimeId);
                    mapFiles = EdMapFiles
                        .Select(f => f.Path == entry ? f with { Content = EdMapSource } : f)
                        .ToList();
                }

                var mcpIds = EdMcpConnectionIds.Select(id => Guid.Parse(id)).ToList();

                var cmd = new UpdateJobCommand(
                    JobId: EditJobId.Value,
                    Name: EdName.Trim(),
                    Description: string.IsNullOrWhiteSpace(EdDescription)
                        ? null
                        : EdDescription.Trim(),
                    MapImage: EdMapSourceKind == "image" ? EdMapImage : null,
                    MapRuntimeId: EdMapSourceKind == "code" ? EdMapRuntimeId : null,
                    MapSource: EdMapSourceKind == "code" && mapFiles is null ? EdMapSource : null,
                    MapFiles: mapFiles,
                    MapEntrypoint: EdMapSourceKind == "code"
                    && !string.IsNullOrWhiteSpace(EdMapEntrypoint)
                        ? EdMapEntrypoint
                        : null,
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
                    AllowApiInvocation: EdAllowApiInvocation,
                    Parameters: parameters,
                    PostJobActions: EdPostJobActions.ToList(),
                    ReturnType: EdReturnType,
                    ReturnFileName: string.IsNullOrWhiteSpace(EdReturnFileName)
                        ? null
                        : EdReturnFileName.Trim(),
                    RetryCount: EdRetryCount,
                    RetryDelaySeconds: EdRetryDelaySeconds,
                    McpConnectionIds: mcpIds
                );

                await _svc.UpdateJobAsync(cmd);
                Message = $"Job '{EdName.Trim()}' updated.";
            }
            else
            {
                var mcpIds = EdMcpConnectionIds.Select(id => Guid.Parse(id)).ToList();

                var cmd = new CreateJobCommand(
                    ProjectId: ProjectId,
                    Name: EdName.Trim(),
                    Description: string.IsNullOrWhiteSpace(EdDescription)
                        ? null
                        : EdDescription.Trim(),
                    MapImage: EdMapSourceKind == "image" ? EdMapImage : null,
                    MapRuntimeId: EdMapSourceKind == "code" ? EdMapRuntimeId : null,
                    MapSource: EdMapSourceKind == "code" ? EdMapSource : null,
                    MapEntrypoint: EdMapSourceKind == "code"
                    && !string.IsNullOrWhiteSpace(EdMapEntrypoint)
                        ? EdMapEntrypoint
                        : null,
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
                    AllowApiInvocation: EdAllowApiInvocation,
                    Parameters: parameters,
                    PostJobActions: EdPostJobActions.ToList(),
                    ReturnType: EdReturnType,
                    ReturnFileName: string.IsNullOrWhiteSpace(EdReturnFileName)
                        ? null
                        : EdReturnFileName.Trim(),
                    RetryCount: EdRetryCount,
                    RetryDelaySeconds: EdRetryDelaySeconds,
                    McpConnectionIds: mcpIds
                );

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
            if (EditJobId == jobId)
                CloseEditor();
            Message = "Job deleted.";
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        NotifyStateChanged();
    }
}
