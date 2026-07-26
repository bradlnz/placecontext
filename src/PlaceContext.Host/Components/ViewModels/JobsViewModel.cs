using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels.Helpers;
using PlaceContext.Infrastructure.Operations;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class JobsViewModel : PageViewModel
{
    private readonly IPlaceContextService _svc;
    private readonly OperationCenter _opCenter;

    public JobsViewModel(IPlaceContextService svc, OperationCenter opCenter)
    {
        _svc = svc;
        _opCenter = opCenter;
    }

    // ── Public state (bound by markup) ─────────────────────────────────────────────────────────
    public Guid ProjectId { get; private set; }

    public IReadOnlyList<JobView>? Jobs { get; private set; }
    public IReadOnlyList<ProjectSecretView>? VaultSecrets { get; private set; }
    public IReadOnlyList<JobRunView>? Runs { get; private set; }
    public JobRunDetailView? RunDetail { get; private set; }
    public IReadOnlyList<RunArtifactLinkView>? RunArtifacts { get; private set; }
    public IReadOnlyList<JobRunTelemetry>? JobTelemetry { get; private set; }
    public Guid? SelectedJobId { get; private set; }
    public Guid? RunningJobId { get; private set; }
    public string? Message { get; private set; }
    public bool Loading { get; private set; } = true;

    // ── Triggers state ────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<TriggerView>? Triggers { get; private set; }
    public IReadOnlyList<EventTypeView>? EventTypes { get; private set; }
    public string TrName { get; set; } = "";
    public string TrKind { get; set; } = "Schedule";
    public string TrCron { get; set; } = "0 0 * * *";
    public string TrEvent { get; set; } = "";
    public bool TrBusy { get; private set; }
    public string? TrError { get; private set; }

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

    // ── Run-input prompt state ────────────────────────────────────────────────────────────────
    public JobView? RunPromptJob { get; private set; }
    public Dictionary<string, string> RunArgs { get; set; } = new();
    public string? RunPromptError { get; private set; }

    // ── Static helpers used by markup ──────────────────────────────────────────────────────────
    public static string DefaultEntrypointFor(string? runtime) => runtime switch
    {
        "python" => "main.py",
        "go" => "main.go",
        "ruby" => "main.rb",
        "dotnet" => "main.cs",
        _ => "index.js",
    };

    public static string SourcePlaceholderFor(string? runtime) => runtime switch
    {
        "python" => "import sys, json\ndata = json.loads(sys.stdin.read() or \"{}\")\nresult = {}\nprint(json.dumps(result))",
        "go" => "package main\n\nimport (\n    \"encoding/json\"\n    \"io\"\n    \"os\"\n)\n\nfunc main() {\n    in, _ := io.ReadAll(os.Stdin)\n    var data any\n    json.Unmarshal(in, &data)\n    json.NewEncoder(os.Stdout).Encode(map[string]any{})\n}",
        "ruby" => "require 'json'\ndata = JSON.parse(STDIN.read)\nresult = {}\nputs result.to_json",
        "dotnet" => "using System.Text.Json;\nvar input = Console.In.ReadToEnd();\nvar data = JsonSerializer.Deserialize<JsonElement>(input);\nvar result = new { };\nConsole.Write(JsonSerializer.Serialize(result));",
        _ => "const fs = require('fs');\nconst data = JSON.parse(fs.readFileSync('/dev/stdin','utf8'));\nconst result = {};\nprocess.stdout.write(JSON.stringify(result));",
    };

    public static string StatusColor(string? status) => StatusHelper.Color(status);
    public static string StatusBg(string? status) => StatusHelper.Background(status);
    public static string FormatDuration(DateTimeOffset start, DateTimeOffset end) => FormatHelper.Duration(start, end);
    public static string FormatMs(double? ms) => FormatHelper.Ms(ms);
    public static string FormatBytes(long n) => FormatHelper.Bytes(n);
    public static string DataUri(RunArtifactView a) => FormatHelper.DataUri(a);
    public static string PrettyJson(string raw) => FormatHelper.PrettyJson(raw);

    public static readonly (PlaceContext.Domain.ValueObjects.PostJobActionKind Kind, string Label)[] PostJobActionChoices =
    {
        (PlaceContext.Domain.ValueObjects.PostJobActionKind.HtmlReport, "HTML report"),
        (PlaceContext.Domain.ValueObjects.PostJobActionKind.Chart, "Chart"),
        (PlaceContext.Domain.ValueObjects.PostJobActionKind.Csv, "CSV export"),
        (PlaceContext.Domain.ValueObjects.PostJobActionKind.RawBundle, "Raw artifacts bundle"),
    };

    public static readonly (PlaceContext.Domain.ValueObjects.JobReturnType Type, string Label)[] ReturnTypeChoices =
    {
        (PlaceContext.Domain.ValueObjects.JobReturnType.Json, "JSON — stored as result.json"),
        (PlaceContext.Domain.ValueObjects.JobReturnType.Table, "Table — rendered as an HTML report"),
        (PlaceContext.Domain.ValueObjects.JobReturnType.Chart, "Chart — rendered as an SVG chart page"),
        (PlaceContext.Domain.ValueObjects.JobReturnType.Html, "HTML — stored openable as-is"),
        (PlaceContext.Domain.ValueObjects.JobReturnType.Csv, "CSV — flattened to a CSV export"),
        (PlaceContext.Domain.ValueObjects.JobReturnType.Text, "Text — stored as result.txt"),
        (PlaceContext.Domain.ValueObjects.JobReturnType.Pdf, "PDF — file emitted to /out, stored as-is"),
        (PlaceContext.Domain.ValueObjects.JobReturnType.Image, "Image — file emitted to /out (png/jpg/svg/…)"),
        (PlaceContext.Domain.ValueObjects.JobReturnType.Video, "Video — file emitted to /out (mp4/webm/…)"),
    };

    public bool IsRunningJob(Guid jobId) => RunningJobId == jobId;

    // ── Lifecycle ─────────────────────────────────────────────────────────────────────────────
    public void Initialize(Guid projectId)
    {
        ProjectId = projectId;
        _opCenter.Changed += OnOpsChanged;
    }

    public void DetachEvents() => _opCenter.Changed -= OnOpsChanged;

    public async Task LoadAsync()
    {
        Loading = true;
        Message = null;
        try
        {
            Jobs = await _svc.ListJobsAsync(ProjectId);
            VaultSecrets = await _svc.ListProjectSecretsAsync(ProjectId);
            Triggers = await _svc.ListTriggersAsync(ProjectId);
            EventTypes = await _svc.ListEventTypesAsync();
        }
        catch (Exception ex) { Message = ex.Message; }
        finally { Loading = false; NotifyStateChanged(); }
    }

    // ── Triggers ──────────────────────────────────────────────────────────────────────────────
    public IEnumerable<TriggerView> JobTriggers() =>
        Triggers?.Where(t => t.JobId == SelectedJobId) ?? Enumerable.Empty<TriggerView>();

    public async Task AddTriggerAsync()
    {
        TrError = null;
        if (!SelectedJobId.HasValue) return;
        if (string.IsNullOrWhiteSpace(TrName)) { TrError = "Name is required."; NotifyStateChanged(); return; }

        TrBusy = true;
        try
        {
            var cron = TrKind == "Schedule" ? TrCron : null;
            var evt = TrKind == "Event" ? TrEvent : null;
            await _svc.CreateTriggerAsync(new CreateTriggerCommand(SelectedJobId.Value, TrName.Trim(), TrKind, cron, evt));
            Triggers = await _svc.ListTriggersAsync(ProjectId);
            TrName = "";
            TrEvent = "";
        }
        catch (Exception ex) { TrError = ex.Message; }
        finally { TrBusy = false; NotifyStateChanged(); }
    }

    public async Task ToggleTriggerAsync(TriggerView t)
    {
        try
        {
            await _svc.SetTriggerEnabledAsync(t.Id, !t.Enabled);
            Triggers = await _svc.ListTriggersAsync(ProjectId);
            NotifyStateChanged();
        }
        catch (Exception ex) { TrError = ex.Message; NotifyStateChanged(); }
    }

    public async Task RemoveTriggerAsync(Guid triggerId)
    {
        try
        {
            await _svc.DeleteTriggerAsync(triggerId);
            Triggers = await _svc.ListTriggersAsync(ProjectId);
            if (SelectedTriggerId == triggerId) SelectedTriggerId = null;
            NotifyStateChanged();
        }
        catch (Exception ex) { TrError = ex.Message; NotifyStateChanged(); }
    }

    public TriggerView? SelectedTrigger() =>
        SelectedTriggerId is { } id ? JobTriggers().FirstOrDefault(t => t.Id == id) : null;

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
            RunArgs = job.Parameters.ToDictionary(p => p.Name, _ => "");
            RunPromptError = null;
            NotifyStateChanged();
            return;
        }
        await RunJobCoreAsync(jobId, null);
    }

    private async Task RunJobCoreAsync(Guid jobId, string? payload)
    {
        Message = null;
        RunningJobId = jobId;
        var tenant = CurrentTenant.Current;
        if (tenant is null) { Message = "No tenant resolved — sign in again."; RunningJobId = null; NotifyStateChanged(); return; }
        var jobName = Jobs?.FirstOrDefault(j => j.Id == jobId)?.Name ?? "job";
        var runId = Guid.NewGuid();
        _opCenter.Run(tenant, ProjectId, $"Run job — {jobName}", $"/observability?run={runId}",
            async (sp, ct) =>
            {
                var result = await sp.GetRequiredService<IPlaceContextService>().RunJobAsync(jobId, payload, runId, ct);
                return $"run finished — {result.Status}";
            },
            correlationKey: RunStatusWatchService.JobRunKey(runId));
        RunPromptJob = null;
        RunningJobId = null;
        Message = $"Run of {jobName} started in the background — follow it in the notifications bell.";
        if (SelectedJobId == jobId)
        {
            try { Runs = await _svc.ListJobRunsAsync(jobId); } catch { }
        }
        NotifyStateChanged();
    }

    public string GetArg(string name) => RunArgs.TryGetValue(name, out var v) ? v : "";
    public void SetArg(string name, string value) => RunArgs[name] = value;

    public async Task SubmitRunPromptAsync()
    {
        if (RunPromptJob is null) return;
        RunPromptError = null;

        var missing = RunPromptJob.Parameters
            .Where(p => p.Required && string.IsNullOrWhiteSpace(GetArg(p.Name)))
            .Select(p => p.Label ?? p.Name)
            .ToList();
        if (missing.Count > 0) { RunPromptError = $"Required: {string.Join(", ", missing)}"; NotifyStateChanged(); return; }

        var payload = System.Text.Json.JsonSerializer.Serialize(
            RunPromptJob.Parameters.ToDictionary(p => p.Name, p => GetArg(p.Name)));
        await RunJobCoreAsync(RunPromptJob.Id, payload);
    }

    public void CancelRunPrompt()
    {
        RunPromptJob = null;
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
            EdPayloadForm = new Dictionary<string, string>();
            try
            {
                var first = EdInputPayloadsRaw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
                if (first is not null)
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(first);
                    if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                        foreach (var prop in doc.RootElement.EnumerateObject())
                            if (prop.Value.ValueKind is not (System.Text.Json.JsonValueKind.Object or System.Text.Json.JsonValueKind.Array))
                                EdPayloadForm[prop.Name] = prop.Value.ValueKind == System.Text.Json.JsonValueKind.String
                                    ? prop.Value.GetString() ?? "" : prop.Value.ToString();
                }
            }
            catch { }
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
                    RetryDelaySeconds: EdRetryDelaySeconds);

                await _svc.UpdateJobAsync(cmd);
                Message = $"Job '{EdName.Trim()}' updated.";
            }
            else
            {
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
                    RetryDelaySeconds: EdRetryDelaySeconds);

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

    // ── Utilities ─────────────────────────────────────────────────────────────────────────────
    public static int[] ParseInts(string raw, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<int>();
        var result = new List<int>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, out var n)) { error = $"'{part}' is not a valid integer."; return Array.Empty<int>(); }
            result.Add(n);
        }
        return result.ToArray();
    }
}

// ── Inner model ──────────────────────────────────────────────────────────────────────────────
public sealed class ParamEdit
{
    public string Name = "";
    public string Label = "";
    public string Type = "text";
    public string OptionsRaw = "";
    public bool Required = true;

    public JobParameterDto ToDto() => new(Name.Trim(),
        string.IsNullOrWhiteSpace(Label) ? null : Label.Trim(), Required, Type,
        Type == "select"
            ? OptionsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : null);

    public static ParamEdit From(JobParameterDto p) => new()
    {
        Name = p.Name,
        Label = p.Label ?? "",
        Type = p.Type,
        OptionsRaw = string.Join(", ", p.Options ?? Array.Empty<string>()),
        Required = p.Required,
    };
}
