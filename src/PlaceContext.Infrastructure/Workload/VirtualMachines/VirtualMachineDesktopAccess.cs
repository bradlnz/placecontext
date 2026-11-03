using System.Collections.Concurrent;
using System.Net.Sockets;
using PlaceContext.Application.Access;
using PlaceContext.Application.Tenancy;
using PlaceContext.Application.Workload.VirtualMachines;
namespace PlaceContext.Infrastructure.Workload;

public sealed class VirtualMachineDesktopAccess(VirtualMachineDesktopSessions sessions, ICurrentTenant tenant, ICurrentUser user,
    IPermissionService permissions) : IVirtualMachineDesktop
{
    private async Task<VirtualMachineDesktopSessions.Session?> FindAsync(VirtualMachineRunContext run, CancellationToken ct)
    {
        if (!tenant.IsResolved || !user.IsAuthenticated || !await permissions.HasAsync(Permission.JobsRun, ct))
        {
            throw new UnauthorizedAccessException("Workflow execution permission is required.");
        }
        return sessions.Find(tenant.TenantId, user.UserId, run);
    }
    private async Task<VirtualMachineDesktopSessions.Session> RequireAsync(VirtualMachineRunContext run, CancellationToken ct) =>
        await FindAsync(run, ct) ?? throw new InvalidOperationException("This desktop is unavailable on this host or belongs to another user.");
    public async Task<VirtualMachineDesktopStatus?> StatusAsync(VirtualMachineRunContext run, CancellationToken ct = default) => (await FindAsync(run, ct))?.Status;
    public async Task TakeControlAsync(VirtualMachineRunContext run, CancellationToken ct = default) => (await RequireAsync(run, ct)).TakeControl();
    public async Task ResumeAsync(VirtualMachineRunContext run, CancellationToken ct = default) => (await RequireAsync(run, ct)).Resume();
    public async Task SaveLoginAsync(VirtualMachineRunContext run, string name, CancellationToken ct = default) => await (await RequireAsync(run, ct)).SaveAsync(name, ct);
    public async Task<Stream> ConnectAsync(VirtualMachineRunContext run, CancellationToken ct = default, bool viewOnly = false) => await (await RequireAsync(run, ct)).ConnectAsync(ct, viewOnly);
}
