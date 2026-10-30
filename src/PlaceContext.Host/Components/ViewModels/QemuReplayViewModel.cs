using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class QemuReplayViewModel(IQemuReplayStore store) : PageViewModel, IComponentViewModel, IDisposable
{
    public IReadOnlyList<ChainStepRunView> Steps { get; private set; } = [];
    public IReadOnlyList<QemuReplayFrame> Frames { get; private set; } = [];
    public int Step { get; private set; }
    public int Position { get; private set; }
    public QemuReplayFrame? Current => Frames.ElementAtOrDefault(Position);
    public QemuReplayFrame? Pointer => Frames.Take(Position + 1).LastOrDefault(frame => frame.PointerX is not null && frame.PointerY is not null);
    public bool Recording => Steps.FirstOrDefault(step => step.Index == Step)?.Status == "Running";
    public ChainStepRunView? ActiveDesktop => Steps.FirstOrDefault(step => step.Status == "Running");
    public bool Playing { get; private set; }
    public bool Loading { get; private set; }
    public bool ImageFailed { get; private set; }
    public string? Error { get; private set; }
    public string? ImageUrl => Current is { HasImage: true } frame
        ? $"/projects/{_projectId}/workflow-runs/{_runId}/qemu/{Step}/frames/{frame.Sequence}" : null;
    public static string Time(long milliseconds) => TimeSpan.FromMilliseconds(milliseconds).ToString(@"mm\:ss");
    private Guid _runId, _projectId;
    private long _version;
    private bool _wasRecording;
    private CancellationTokenSource? _load, _play;

    public async Task SetRunAsync(ChainRunView run)
    {
        var changed = _runId != run.Id;
        if (changed)
        {
            Stop();
            _runId = run.Id; _projectId = run.ProjectId;
            Frames = []; Position = 0; Error = null; ImageFailed = false;
        }
        Steps = run.Steps.Where(step => step.ActionType == QemuChainAction.ActionType).ToArray();
        if (Steps.Count == 0) return;
        if (changed || !Steps.Any(step => step.Index == Step)) Step = Steps[0].Index;
        var refresh = changed || Recording || _wasRecording;
        _wasRecording = Recording;
        if (refresh) await RefreshAsync();
    }

    public async Task SelectStepAsync(string? value)
    {
        if (!int.TryParse(value, out var step) || !Steps.Any(item => item.Index == step) || step == Step) return;
        Stop(); Step = step; Frames = []; Position = 0; ImageFailed = false;
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        _load?.Cancel(); _load?.Dispose();
        _load = new(); var ct = _load.Token;
        var version = ++_version;
        Loading = true; Error = null; ImageFailed = false;
        try
        {
            var frames = await store.ListAsync(_projectId, _runId, Step, ct);
            if (version != _version || ct.IsCancellationRequested) return;
            var follow = !Playing && (Recording || Frames.Count > 0) && Position >= Frames.Count - 1;
            Frames = frames;
            Position = follow ? Math.Max(0, frames.Count - 1) : Math.Clamp(Position, 0, Math.Max(0, frames.Count - 1));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch { if (version == _version) Error = "Could not load the replay. Try refreshing it."; }
        finally { if (version == _version) { Loading = false; NotifyStateChanged(); } }
    }

    public void Select(int position)
    {
        Stop(); Position = Math.Clamp(position, 0, Math.Max(0, Frames.Count - 1));
        ImageFailed = false; NotifyStateChanged();
    }
    public void Seek(string? value) { if (int.TryParse(value, out var position)) Select(position); }
    public void MarkImageFailed() { ImageFailed = true; NotifyStateChanged(); }
    public void Stop()
    { _play?.Cancel(); _play?.Dispose(); _play = null; Playing = false; }

    public async Task TogglePlaybackAsync()
    {
        if (Playing) { Stop(); NotifyStateChanged(); return; }
        if (Frames.Count < 2) return;
        if (Position >= Frames.Count - 1) Position = 0;
        Playing = true; ImageFailed = false; _play = new();
        var ct = _play.Token;
        NotifyStateChanged();
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (Position >= Frames.Count - 1) break;
                Position++; ImageFailed = false; NotifyStateChanged();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { if (!ct.IsCancellationRequested) { Stop(); NotifyStateChanged(); } }
    }
    public void Dispose() { ++_version; _load?.Cancel(); _load?.Dispose(); Stop(); Detach(); }
}
