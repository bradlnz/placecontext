using PlaceContext.Application.Dtos;
using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class ChainPipelineViewModel : PageViewModel, IComponentViewModel, IDisposable
{
    public IReadOnlyList<IReadOnlyList<ChainStepRunView>> Stages(ChainRunView run)
    {
        return run.StepsByStage;
    }

    public string Icon(string status)
    {
        return ScopedPresentationCatalog.StepStatus(status) switch
        {
            ChainStepStatus.Succeeded => "✓",
            ChainStepStatus.Failed => "✗",
            ChainStepStatus.Partial => "~",
            ChainStepStatus.Running => "●",
            ChainStepStatus.Skipped => "⏭",
            _ => "○",
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

    public string CursorStyle(ChainStepRunView step) =>
        HasRun(step) ? "cursor:pointer" : string.Empty;

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
