using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class ChainCanvasViewModel : PageViewModel, IComponentViewModel, IDisposable
{
    private string _pathJobId = string.Empty;
    private int? _draggedStage;
    private (int Stage, int Branch)? _draggedPath;
    private int? _dragOverStage;

    public IReadOnlyList<JobChainStageView> Stages { get; private set; } = [];
    public IReadOnlyList<JobView> Jobs { get; private set; } = [];
    public IReadOnlyDictionary<int, ChainGateView> Gates
    {
        get; private set;
    } =
        new Dictionary<int, ChainGateView>();
    public string PathJobId => _pathJobId;
    public int? PathPickerStage
    {
        get; private set;
    }
    public int? DragOverStage => _dragOverStage;
    public bool IsPathEnabled => Guid.TryParse(_pathJobId, out _);

    public void SetParameters(
        IReadOnlyList<JobChainStageView> stages,
        IReadOnlyList<JobView> jobs,
        IReadOnlyDictionary<int, ChainGateView> gates
    )
    {
        Stages = stages;
        Jobs = jobs;
        Gates = gates;
    }

    public void SetPathJobId(string? value)
    {
        _pathJobId = value ?? string.Empty;
    }

    public void OpenPathPicker(int stageIndex)
    {
        PathPickerStage = stageIndex;
        _pathJobId = string.Empty;
    }

    public void ClosePathPicker()
    {
        PathPickerStage = null;
        _pathJobId = string.Empty;
    }

    public Guid? ConsumePathJob()
    {
        Guid? result = Guid.TryParse(_pathJobId, out var id) ? id : null;
        if (result is not null)
        {
            ClosePathPicker();
        }
        return result;
    }

    public void BeginStageDrag(int stageIndex)
    {
        _draggedStage = stageIndex;
        _draggedPath = null;
    }

    public void BeginPathDrag(int stageIndex, int branchIndex)
    {
        _draggedPath = (stageIndex, branchIndex);
        _draggedStage = null;
    }

    public void DragOver(int stageIndex) => _dragOverStage = stageIndex;

    public void DragLeave(int stageIndex)
    {
        if (_dragOverStage == stageIndex)
        {
            _dragOverStage = null;
        }
    }

    public (int SourceStage, int SourceBranch, int TargetStage)? ConsumePathDrop(int targetStage)
    {
        if (_draggedPath is not { } path)
        {
            return null;
        }
        (int SourceStage, int SourceBranch, int TargetStage)? result = (
            path.Stage,
            path.Branch,
            targetStage
        );
        EndDrag();
        return result;
    }

    public (int SourceStage, int TargetStage)? ConsumeStageDrop(int targetStage)
    {
        (int SourceStage, int TargetStage)? result = _draggedStage is { } source
            ? (source, targetStage)
            : null;
        EndDrag();
        return result;
    }

    public void EndDrag()
    {
        _draggedStage = null;
        _draggedPath = null;
        _dragOverStage = null;
    }

    public string GateIcon(ChainGateView gate) =>
        gate switch
        {
            WaitGateView => "clock",
            ConditionGateView => "split",
            _ => "about",
        };

    public string GateClass(ChainGateView gate) =>
        gate switch
        {
            WaitGateView => "gate-wait",
            ConditionGateView => "gate-condition",
            _ => string.Empty,
        };

    public static string StageIcon(JobChainStageView stage) => stage.Action switch
    {
        SendEmailChainActionView => "mail",
        SendSmsChainActionView => "message",
        VirtualMachineChainActionView => "vm",
        ContainerBuildChainActionView or ContainerRunChainActionView => "code",
        QueryDataChainActionView => "database",
        ScoringGateChainActionView => "check",
        LoopChainActionView loop => loop.Kind == "humanInput" ? "human" : "agent",
        null => "code",
        _ => "event",
    };

    public static string StageTitle(JobChainStageView stage) => stage.Action switch
    {
        SubWorkflowChainActionView child => child.WorkflowName,
        ParallelWorkflowsChainActionView => "Run matching workflows in parallel",
        VirtualMachineChainActionView => "Browser automation",
        LoopChainActionView { AgentProvider: "jev" } => "AI verification",
        null => stage.IsParallel ? "Run jobs in parallel" : stage.Jobs.FirstOrDefault()?.JobName ?? "Run job",
        _ => stage.Action.DisplayName
    };

    public string StageDescription(JobChainStageView stage) => stage.Action switch
    {
        LoopChainActionView agent => agent.Instructions,
        VirtualMachineChainActionView browser => browser.Command,
        SubWorkflowChainActionView child => $"Pass the current context to {child.WorkflowName} and continue with its result.",
        ParallelWorkflowsChainActionView parallel => $"Check {parallel.Branches.Count} IF branches, run every matching workflow, then join their results.",
        RepeatFlowChainActionView repeat => $"Retry the preceding process until the condition passes. Fail after {repeat.MaxAttempts} total attempts.",
        QueryDataChainActionView query => $"Read project data into {query.OutputField} for the following steps.",
        ScoringGateChainActionView score => $"Evaluate {score.Criteria.Count} criteria against {score.InputField}; require a score of {score.PassScore}.",
        SendEmailChainActionView email => $"Send “{email.Subject}” to {email.Recipient}.",
        SendSmsChainActionView sms => $"Send a text message to {sms.Recipient}.",
        ContainerBuildChainActionView build => $"Build an image from {build.ContextField} and save it in {build.OutputField}.",
        ContainerRunChainActionView container => $"Run the container task and save its result in {container.OutputField}.",
        null when stage.IsParallel => $"Run {string.Join(", ", stage.Jobs.Select(job => job.JobName))} together and wait for all results.",
        null => Jobs.FirstOrDefault(job => job.Id == stage.Jobs.FirstOrDefault()?.JobId)?.Description is { Length: > 0 } description
            ? description : "Run this job with the current workflow context and pass its result to the next step.",
        _ => "Run this action with the current workflow context."
    };

    public string GateLabel(ChainGateView gate) =>
        gate switch
        {
            WaitGateView wait => $"{wait.DurationSeconds:0.#}s",
            ConditionGateView { ReviewQuestion: not null } => "AI IF",
            ConditionGateView => "IF",
            _ => "",
        };

    public string GateTooltip(ChainGateView gate) =>
        gate switch
        {
            WaitGateView wait => $"Wait {wait.DurationSeconds:0.#} second(s) before this stage",
            ConditionGateView { ReviewQuestion: { } question } => $"AI Review: {question}",
            ConditionGateView condition => $"Run when {condition.Expression}",
            _ => "",
        };

    public void Dispose() => Detach();
}
