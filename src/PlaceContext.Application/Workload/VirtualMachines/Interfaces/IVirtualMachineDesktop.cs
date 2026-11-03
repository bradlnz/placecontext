namespace PlaceContext.Application.Workload.VirtualMachines;

public interface IVirtualMachineDesktop
{
    Task<VirtualMachineDesktopStatus?> StatusAsync(VirtualMachineRunContext run, CancellationToken ct = default);
    Task TakeControlAsync(VirtualMachineRunContext run, CancellationToken ct = default);
    Task ResumeAsync(VirtualMachineRunContext run, CancellationToken ct = default);
    Task SaveLoginAsync(VirtualMachineRunContext run, string name, CancellationToken ct = default);
    Task<Stream> ConnectAsync(VirtualMachineRunContext run, CancellationToken ct = default, bool viewOnly = false);
}
