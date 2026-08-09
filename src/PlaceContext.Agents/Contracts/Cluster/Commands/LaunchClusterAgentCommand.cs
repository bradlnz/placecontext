using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Cluster;

/// <summary>
/// Mints a fresh, single-use Tailscale auth key from the OAuth client credentials stored in the
/// vault (under <see cref="SystemProjects.Cluster"/>) and folds it into a join code, so a brand-new
/// agent machine can join the tailnet and the k3s fleet in one step — no manually-copied, long-lived
/// auth key required.
/// </summary>
public sealed record LaunchClusterAgentCommand : ICommand<LaunchAgentResult>, IRequiresPermission
{
    string IRequiresPermission.RequiredPermission => Permission.SettingsManage;
}
