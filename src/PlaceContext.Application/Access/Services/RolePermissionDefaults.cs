using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Access;

/// <summary>
/// Pure role → permission mapping: what each <see cref="UserRole"/> grants by default, before any
/// tenant-scoped override is applied. No I/O — safe to unit-test in isolation.
///
/// Viewer is read-only. Member does the actual work (runs/edits jobs, chains, triggers, data,
/// artifacts, events) but not the workspace-administration bucket. Admin and Owner get the full
/// catalog — the coarser Owner/Admin/Member/Viewer ladder (still enforced by the "Owner" policy) is
/// kept for the handful of whole-tenant actions that sit outside this fine-grained catalog entirely
/// (e.g. anything that would touch the tenant record itself).
/// </summary>
public static class RolePermissionDefaults
{
    /// <summary>The administration bucket an ordinary Member does not get by default — an Admin/Owner
    /// grants one of these to a Member via an explicit override, or promotes them.</summary>
    private static readonly IReadOnlySet<string> AdminOnly = new HashSet<string>
    {
        Permission.SecretsManage,
        Permission.BackupManage,
        Permission.MembersManage,
        Permission.SettingsManage,
    };

    private static readonly IReadOnlySet<string> ViewerDefaults = new HashSet<string>
    {
        Permission.ProjectsView,
        Permission.JobsView,
        Permission.DataRead,
        Permission.ArtifactsView,
    };

    private static readonly IReadOnlySet<string> CrmUserDefaults = new HashSet<string>
    {
        Permission.CrmView,
        Permission.CrmCommsSend,
        Permission.CrmOpportunityWrite,
        Permission.CrmAutomationAssign,
    };

    private static readonly IReadOnlySet<string> MemberDefaults =
        new HashSet<string>(Permission.All.Where(p => !AdminOnly.Contains(p)));

    private static readonly IReadOnlySet<string> FullCatalog = new HashSet<string>(Permission.All);

    public static IReadOnlySet<string> GetDefaults(UserRole role) => role switch
    {
        UserRole.Viewer => ViewerDefaults,
        UserRole.CrmUser => CrmUserDefaults,
        UserRole.Member => MemberDefaults,
        UserRole.Admin => FullCatalog,
        UserRole.Owner => FullCatalog,
        _ => new HashSet<string>(),
    };
}
