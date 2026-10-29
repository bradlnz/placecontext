using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class ChainPipelineViewModel(PlaceContextService? service = null) : PageViewModel, IComponentViewModel, IDisposable
{
    private Guid? _runId;
    private ChainRunView? _latestRun;
    private JobChainView? _definition;
    public IReadOnlyList<JobChainStageView>? Layout { get; private set; }
    public Dictionary<string, IReadOnlyList<ChainStepRunView>>? RunSteps { get; private set; }

    public async Task LoadAsync(ChainRunView run)
    {
        _latestRun = run;
        if (_runId != run.Id)
        {
            _runId = run.Id;
            _definition = null;
            Layout = null;
            RunSteps = null;
            try
            {
                var chains = service is null ? [] : await service.ListJobChainsAsync(run.ProjectId);
                if (_runId != run.Id) return;
                _definition = chains.FirstOrDefault(chain => chain.Id == run.ChainId);
            }
            catch
            {
                // Recorded steps remain available if the definition was deleted or cannot be loaded.
            }
        }
        if (_runId != run.Id || _latestRun is not { } latest) return;
        RunSteps = _definition is null ? null : MatchSteps(latest, _definition.Stages);
        Layout = RunSteps is null ? null : _definition!.Stages;
    }

    // shortcut: runs lack layout snapshots; match current stages conservatively, add snapshots for historical gate expressions.
    public static Dictionary<string, IReadOnlyList<ChainStepRunView>>? MatchSteps(
        ChainRunView run, IReadOnlyList<JobChainStageView> stages)
    {
        var recorded = run.StepsByStage.ToDictionary(group => group[0].StageIndex);
        var mapped = new Dictionary<string, IReadOnlyList<ChainStepRunView>>();
        var alternatives = new Dictionary<string, JobChainStageView>();
        for (var index = 0; index < stages.Count; index++)
        {
            if (!recorded.Remove(index, out var steps) || !Matches(stages[index], steps)) return null;
            mapped[index.ToString()] = steps;
            CollectAlternatives(stages[index], index.ToString());
        }
        foreach (var steps in recorded.Values)
        {
            var candidates = alternatives.Where(pair => Matches(pair.Value, steps)).ToList();
            if (candidates.Count != 1) return null;
            var candidate = candidates[0];
            mapped[candidate.Key] = steps;
            alternatives.Remove(candidate.Key);
        }
        foreach (var (path, stage) in alternatives)
        {
            var status = run.FinishedAt.HasValue ? "Not run" : "Pending";
            mapped[path] = stage.Action is { } action
                ? [new(-1, -1, 0, Guid.Empty, action.DisplayName, null, status, null, null, action.Type)]
                : stage.Jobs.Select((job, index) => new ChainStepRunView(-1, -1, index,
                    job.JobId, job.JobName, null, status, null, null)).ToList();
        }
        return mapped;

        void CollectAlternatives(JobChainStageView stage, string parent)
        {
            var other = stage.ElseBranch ?? (stage.Gate as ConditionGateView)?.ElseBranch ?? [];
            for (var index = 0; index < other.Count; index++)
            {
                var path = $"{parent}/else/{index}";
                alternatives[path] = other[index];
                CollectAlternatives(other[index], path);
            }
        }
    }

    private static bool Matches(JobChainStageView stage, IReadOnlyList<ChainStepRunView> steps) =>
        stage.Action is { } action
            ? steps.Count == 1 && steps[0].ActionType == action.Type
            : steps.Count == stage.Jobs.Count && steps.Select((step, index) =>
                step.ActionType is null && step.JobId == stage.Jobs[index].JobId && step.BranchIndex == index).All(match => match);

    public IReadOnlyList<IReadOnlyList<ChainStepRunView>> Stages(ChainRunView run)
    {
        return run.StepsByStage.OrderBy(group => group.Min(step => step.StartedAt ?? step.FinishedAt ?? DateTimeOffset.MaxValue)).ToList();
    }

    public string Icon(string status)
    {
        return ScopedPresentationCatalog.StepStatus(status) switch
        {
            ChainStepStatus.Succeeded => "check",
            ChainStepStatus.Failed => "close",
            ChainStepStatus.Partial => "warning",
            ChainStepStatus.Running => "refresh",
            ChainStepStatus.Skipped => "skip",
            _ => "circle",
        };
    }

    public string Border(string status)
    {
        var typed = ScopedPresentationCatalog.StepStatus(status);
        if (typed == ChainStepStatus.Running)
            return "var(--brand-2)";
        if (typed is ChainStepStatus.Pending or ChainStepStatus.Skipped)
            return "var(--border)";
        return Color(status);
    }

    public string Color(string status) => StatusHelper.Color(status);

    public string Background(string status) => StatusHelper.Background(status);

    public string Summary(ChainStepRunView step)
    {
        if (ScopedPresentationCatalog.StepStatus(step.Status) == ChainStepStatus.Running)
            return "running…";
        if (step.StartedAt.HasValue && step.FinishedAt.HasValue)
            return $"{step.Status} {Duration(step.StartedAt.Value, step.FinishedAt.Value)}";
        return step.Status.ToLowerInvariant();
    }

    public bool HasRun(ChainStepRunView step) => step.RunId is not null;

    public bool CanCancel(ChainStepRunView step) =>
        step.RunId.HasValue
        && ScopedPresentationCatalog.StepStatus(step.Status) == ChainStepStatus.Running;

    public string Duration(DateTimeOffset start, DateTimeOffset end)
    {
        var span = end - start;
        return span.TotalSeconds < 60
            ? $"{(int)span.TotalSeconds}s"
            : $"{(int)span.TotalMinutes}m {span.Seconds}s";
    }

    public void Dispose() => Detach();
}
