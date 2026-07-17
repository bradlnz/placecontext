namespace PlaceContext.Application.Cluster;

/// <summary>
/// Reserved, non-user-facing project ids used to scope vault secrets (<c>job_secrets</c>) that belong
/// to the platform itself rather than to any tenant-created project — e.g. the Tailscale OAuth client
/// credentials used to mint agent join keys (see <c>LaunchClusterAgentCommand</c>). <c>job_secrets</c>
/// has no foreign key to the <c>projects</c> table (composite key is (ProjectId, Name) only), so these
/// ids are safe to use without a matching project row; rows remain tenant-scoped as usual.
/// </summary>
public static class SystemProjects
{
    /// <summary>Vault scope for cluster/fleet secrets (Tailscale OAuth client id/secret, join tags).</summary>
    public static readonly Guid Cluster = new("00000000-0000-0000-0000-0000000c1c17");
}
