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
    public bool IsDetailsTab => EditorTab.Equals("details", StringComparison.OrdinalIgnoreCase);
    public bool IsRunsTab => EditorTab.Equals("runs", StringComparison.OrdinalIgnoreCase);
    public bool IsCanvasView => EditorView.Equals("canvas", StringComparison.OrdinalIgnoreCase);

    public bool HasStageAction(int stageIndex) => EdStageActions.ContainsKey(stageIndex);

    public bool IsWaitGateEditor =>
        GateEditorType.Equals("wait", StringComparison.OrdinalIgnoreCase);
    public bool IsConditionGateEditor =>
        GateEditorType.Equals("condition", StringComparison.OrdinalIgnoreCase);
    public bool IsInGateOperator =>
        GateEditorOperator.Equals("in", StringComparison.OrdinalIgnoreCase);

    public bool IsChainRunActive(string? status) =>
        ScopedPresentationCatalog.IsQueuedOrRunning(status);

    public bool IsRunning(string? status) => ScopedPresentationCatalog.IsRunning(status);

    public bool IsRunningStep(string? status) =>
        ScopedPresentationCatalog.StepStatus(status) == ChainStepStatus.Running;

    public bool IsFailedStep(string? status) =>
        ScopedPresentationCatalog.StepStatus(status) == ChainStepStatus.Failed;

    public int SucceededSteps(ChainRunView run) =>
        run.Steps.Count(s =>
            ScopedPresentationCatalog.StepStatus(s.Status) == ChainStepStatus.Succeeded
        );

    public string StepOutputTitle(string? status) =>
        IsRunningStep(status) ? "Live output" : "Job output";

    public bool IsDeletedJob(string? name) =>
        string.Equals(name, "(deleted)", StringComparison.Ordinal);

    public string JobChipColor(string? name) =>
        IsDeletedJob(name) ? "var(--bad)" : "var(--brand-2)";

    public string ChainStatusAria(bool running) => running ? "Running" : "Pipeline";

    public string ReduceStatusLabel(bool succeeded) => succeeded ? "Succeeded" : "Failed";

    public Task ChooseConditionNode() => ChooseStageNode("condition");

    public Task ChooseWaitNode() => ChooseStageNode("wait");

    // ── Helpers used by markup ────────────────────────────────────────────────────────────────
    /// <summary>UI-only form key (<c>step0:address</c>) — never sent as a job stdin key.</summary>
    public static string ArgKey(int stepIndex, string param) =>
        ParameterPromptState.ChainArgKey(stepIndex, param);

    public string JobName(Guid jobId) =>
        Jobs?.FirstOrDefault(j => j.Id == jobId)?.Name ?? jobId.ToString("N")[..8];

    public static string StatusColor(string status) => StatusHelper.Color(status);

    public static string StatusBg(string status) => StatusHelper.Background(status);

    public static string FormatDuration(DateTimeOffset start, DateTimeOffset end) =>
        FormatHelper.Duration(start, end);

    public static string PrettyJson(string raw) => FormatHelper.PrettyJson(raw);
}
