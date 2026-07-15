using PlaceContext.Application.Access;
using PlaceContext.Application.Ports;
using Xunit;

namespace PlaceContext.Application.Tests;

/// <summary>Unit tests for the pure permission-resolution logic: role → default permission set, and
/// effective permissions once tenant-scoped overrides are layered on top. No I/O involved.</summary>
public class AccessPermissionsTests
{
    // ── Role → default permission mapping ────────────────────────────────────────────────────────

    [Fact]
    public void Viewer_defaults_are_view_and_read_only()
    {
        var defaults = RolePermissionDefaults.GetDefaults(UserRole.Viewer);

        Assert.Equal(
            new HashSet<string> { Permission.ProjectsView, Permission.JobsView, Permission.DataRead, Permission.ArtifactsView },
            defaults);
    }

    [Fact]
    public void Member_defaults_exclude_the_admin_only_bucket()
    {
        var defaults = RolePermissionDefaults.GetDefaults(UserRole.Member);

        // Does the work: runs/edits jobs, chains, triggers, data, artifacts, events.
        Assert.Contains(Permission.JobsEdit, defaults);
        Assert.Contains(Permission.JobsRun, defaults);
        Assert.Contains(Permission.JobsReplay, defaults);
        Assert.Contains(Permission.ChainsManage, defaults);
        Assert.Contains(Permission.TriggersManage, defaults);
        Assert.Contains(Permission.DataWrite, defaults);
        Assert.Contains(Permission.ArtifactsDelete, defaults);
        Assert.Contains(Permission.EventsManage, defaults);

        // But not the workspace-administration bucket.
        Assert.DoesNotContain(Permission.SecretsManage, defaults);
        Assert.DoesNotContain(Permission.BackupManage, defaults);
        Assert.DoesNotContain(Permission.MembersManage, defaults);
        Assert.DoesNotContain(Permission.SettingsManage, defaults);
    }

    [Fact]
    public void Admin_defaults_are_the_full_catalog()
    {
        var defaults = RolePermissionDefaults.GetDefaults(UserRole.Admin);
        Assert.Equal(Permission.All.Count, defaults.Count);
        Assert.All(Permission.All, p => Assert.Contains(p, defaults));
    }

    [Fact]
    public void Owner_defaults_are_the_full_catalog()
    {
        var defaults = RolePermissionDefaults.GetDefaults(UserRole.Owner);
        Assert.Equal(Permission.All.Count, defaults.Count);
        Assert.All(Permission.All, p => Assert.Contains(p, defaults));
    }

    // ── Effective permission resolution (defaults + overrides) ──────────────────────────────────

    [Fact]
    public void Effective_permissions_with_no_overrides_equal_the_role_defaults()
    {
        var overrides = new Dictionary<string, bool>();
        var effective = EffectivePermissionsResolver.Resolve(UserRole.Member, overrides);

        Assert.Equal(RolePermissionDefaults.GetDefaults(UserRole.Member), effective);
    }

    [Fact]
    public void Allow_override_adds_a_permission_the_role_would_not_otherwise_have()
    {
        // A Viewer's defaults never include secrets.manage — an explicit allow grants it anyway.
        var overrides = new Dictionary<string, bool> { [Permission.SecretsManage] = true };
        var effective = EffectivePermissionsResolver.Resolve(UserRole.Viewer, overrides);

        Assert.Contains(Permission.SecretsManage, effective);
        // The rest of the Viewer defaults are untouched.
        Assert.Contains(Permission.ProjectsView, effective);
    }

    [Fact]
    public void Revoke_override_removes_a_permission_the_role_would_otherwise_have()
    {
        // A Member's defaults include jobs.edit — an explicit revoke removes it.
        var overrides = new Dictionary<string, bool> { [Permission.JobsEdit] = false };
        var effective = EffectivePermissionsResolver.Resolve(UserRole.Member, overrides);

        Assert.DoesNotContain(Permission.JobsEdit, effective);
        // Unrelated Member defaults stay granted.
        Assert.Contains(Permission.JobsRun, effective);
    }

    [Fact]
    public void Revoke_wins_over_a_role_default_that_would_otherwise_grant_the_permission()
    {
        // Admin's defaults grant everything, including members.manage — revoke still wins.
        var overrides = new Dictionary<string, bool> { [Permission.MembersManage] = false };
        var effective = EffectivePermissionsResolver.Resolve(UserRole.Admin, overrides);

        Assert.DoesNotContain(Permission.MembersManage, effective);
        Assert.Contains(Permission.SettingsManage, effective); // every other default permission is untouched
    }

    [Fact]
    public void Multiple_overrides_combine_allow_and_revoke_independently()
    {
        var overrides = new Dictionary<string, bool>
        {
            [Permission.SecretsManage] = true,   // Viewer wouldn't otherwise get this — added
            [Permission.ProjectsView] = false,   // Viewer would otherwise get this — removed
        };
        var effective = EffectivePermissionsResolver.Resolve(UserRole.Viewer, overrides);

        Assert.Contains(Permission.SecretsManage, effective);
        Assert.DoesNotContain(Permission.ProjectsView, effective);
        Assert.Contains(Permission.JobsView, effective); // untouched Viewer default
    }
}
