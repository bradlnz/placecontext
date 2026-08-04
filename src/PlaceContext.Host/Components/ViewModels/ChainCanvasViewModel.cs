using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class ChainCanvasViewModel : PageViewModel, IComponentViewModel, IDisposable
{
    private string _addJobId = string.Empty;
    private string _pathJobId = string.Empty;
    private int? _draggedStage;
    private (int Stage, int Branch)? _draggedPath;
    private int? _dragOverStage;

    public IReadOnlyList<JobChainStageView> Stages { get; private set; } = [];
    public IReadOnlyList<JobView> Jobs { get; private set; } = [];
    public IReadOnlyDictionary<int, ChainGateView> Gates { get; private set; } =
        new Dictionary<int, ChainGateView>();
    public string AddJobId => _addJobId;
    public string PathJobId => _pathJobId;
    public int? PathPickerStage { get; private set; }
    public int? DragOverStage => _dragOverStage;
    public bool IsAddStageEnabled => Guid.TryParse(_addJobId, out _);
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

    public void SetAddJobId(string? value)
    {
        _addJobId = value ?? string.Empty;
    }

    public void SetPathJobId(string? value)
    {
        _pathJobId = value ?? string.Empty;
    }

    public Guid? ConsumeAddStageJob()
    {
        Guid? result = Guid.TryParse(_addJobId, out var id) ? id : null;
        if (result is not null)
            _addJobId = string.Empty;
        return result;
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
            ClosePathPicker();
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
            _dragOverStage = null;
    }

    public (int SourceStage, int SourceBranch, int TargetStage)? ConsumePathDrop(int targetStage)
    {
        if (_draggedPath is not { } path)
            return null;

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
            WaitGateView => "◷",
            ConditionGateView => "◇",
            _ => "?",
        };

    public string GateClass(ChainGateView gate) =>
        gate switch
        {
            WaitGateView => "gate-wait",
            ConditionGateView => "gate-condition",
            _ => string.Empty,
        };

    public string GateLabel(ChainGateView gate) =>
        gate switch
        {
            WaitGateView wait => $"{wait.DurationSeconds:0.#}s",
            ConditionGateView => "IF",
            _ => "",
        };

    public string GateTooltip(ChainGateView gate) =>
        gate switch
        {
            WaitGateView wait => $"Wait {wait.DurationSeconds:0.#} second(s) before this stage",
            ConditionGateView condition => $"Run when {condition.Expression}",
            _ => "",
        };

    public void Dispose() => Detach();
}
