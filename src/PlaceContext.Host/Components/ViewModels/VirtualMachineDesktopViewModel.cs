using Microsoft.JSInterop;
using PlaceContext.Application.Workload.VirtualMachines;
namespace PlaceContext.Host.Components.ViewModels;

public sealed class VirtualMachineDesktopViewModel(IVirtualMachineDesktop desktop, IJSRuntime js) : PageViewModel, IComponentViewModel, IAsyncDisposable
{
    public string ElementId { get; } = "qemu-desktop-" + Guid.NewGuid().ToString("N");
    public VirtualMachineDesktopStatus? Status
    {
        get; private set;
    }
    public string? Error
    {
        get; private set;
    }
    public string LoginName { get; set; } = "";
    public bool Busy
    {
        get; private set;
    }
    private VirtualMachineRunContext? _run;
    private CancellationTokenSource? _poll;
    private IJSObjectReference? _module;
    private bool _disposed;
    public async Task SetRunAsync(VirtualMachineRunContext run)
    {
        if (_run == run)
        {
            return;
        }
        _poll?.Cancel();
        _poll?.Dispose();
        _poll = new();
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("disconnect", ElementId);
        }
        _run = run;
        Status = null;
        Error = null;
        await RefreshAsync(run, _poll.Token);
        _ = PollAsync(run, _poll.Token);
    }
    private async Task PollAsync(VirtualMachineRunContext run, CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            while (await timer.WaitForNextTickAsync(ct))
            {
                await RefreshAsync(run, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
    private async Task RefreshAsync(VirtualMachineRunContext run, CancellationToken ct)
    {
        try
        {
            var status = await desktop.StatusAsync(run, ct);
            if (ct.IsCancellationRequested || _run != run || _disposed)
            {
                return;
            }
            if (Status != status)
            {
                Status = status;
                NotifyStateChanged();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (UnauthorizedAccessException)
        {
            if (_run == run)
            {
                Status = null;
            }
        }
        catch
        {
            if (_run == run)
            {
                Error = "Could not reach the live desktop. Try again.";
                NotifyStateChanged();
            }
        }
    }
    private async Task ActAsync(Func<VirtualMachineRunContext, CancellationToken, Task> action)
    {
        if (_run is not { } run || Busy || _disposed)
        {
            return;
        }
        Busy = true;
        Error = null;
        NotifyStateChanged();
        try
        {
            await action(run, _poll!.Token);
            await RefreshAsync(run, _poll.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {
            Error = ex.Message;
        }
        catch
        {
            Error = "The desktop action failed. Try again.";
        }
        finally
        {
            Busy = false;
            NotifyStateChanged();
        }
    }
    public Task TakeControlAsync() => ActAsync(desktop.TakeControlAsync);
    public Task ResumeAsync() => ActAsync(desktop.ResumeAsync);
    public Task SaveLoginAsync() => ActAsync((run, ct) => desktop.SaveLoginAsync(run, LoginName, ct));
    public async Task ReconnectAsync()
    {
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("disconnect", ElementId);
        }
        Error = null;
        NotifyStateChanged();
    }
    public async Task AfterRenderAsync()
    {
        if (_disposed)
        {
            return;
        }
        try
        {
            async Task UpdateConnectionAsync()
            {
                if (Status?.State is "Running" or "Pausing" or "Human" or "Saving" && _run is { } run)
                {
                    _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./pcdesktop.js");
                    await _module.InvokeVoidAsync("connect", ElementId, $"/projects/{run.ProjectId}/workflow-runs/{run.RunId}/qemu/{run.Step}/desktop", Status.State == "Human");
                    return;
                }
                if (_module is not null)
                {
                    await _module.InvokeVoidAsync("disconnect", ElementId);
                }
            }
            await UpdateConnectionAsync();
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            if (!_disposed && Error is null)
            {
                Error = "Could not connect the desktop. Use Reconnect to try again.";
                NotifyStateChanged();
            }
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _poll?.Cancel();
        _poll?.Dispose();
        Detach();
        if (_module is not null)
        {
            try
            {
                await _module.InvokeVoidAsync("disconnect", ElementId);
                await _module.DisposeAsync();
            }
            catch (Exception ex) when (ex is JSException or TaskCanceledException) { }
        }
    }
}
