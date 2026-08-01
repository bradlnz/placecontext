using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

/// <summary>Handler tests for the member permission matrix: role grant sets come from role_definitions
/// (with the hardcoded defaults as fallback), and the default admin's settings.manage is protected.</summary>
public class AccessOverrideHandlerTests
{
    private static MemberView Member(string role, bool isDefaultAdmin = false)
        => new(Guid.NewGuid(), $"{Guid.NewGuid():N}@example.com", "Member", role, isDefaultAdmin, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Revoking_settings_manage_from_the_default_admin_is_refused()
    {
        var defaultAdmin = Member("Owner", isDefaultAdmin: true);
        var handler = new SetUserPermissionOverrideHandler(
            new StubMembershipService(defaultAdmin), new InMemoryGrantRepository(),
            new InMemoryRoleDefinitionRepository(), new RecordingUnitOfWork());

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            new SetUserPermissionOverrideCommand(defaultAdmin.Id, Permission.SettingsManage, false)));
    }

    [Fact]
    public async Task Revoking_settings_manage_from_another_admin_is_allowed()
    {
        var admin = Member("Admin");
        var grants = new InMemoryGrantRepository();
        var handler = new SetUserPermissionOverrideHandler(
            new StubMembershipService(admin), grants, new InMemoryRoleDefinitionRepository(), new RecordingUnitOfWork());

        var view = await handler.HandleAsync(
            new SetUserPermissionOverrideCommand(admin.Id, Permission.SettingsManage, false));

        var row = view.Permissions.Single(p => p.Permission == Permission.SettingsManage);
        Assert.False(row.Override);
        Assert.False(row.Effective);
    }

    [Fact]
    public async Task The_matrix_uses_the_role_grant_set_from_role_definitions()
    {
        // Admin edited in the DB to lose jobs.edit — the matrix must reflect that, not the hardcoded
        // full catalog.
        var admin = Member("Admin");
        var roles = new InMemoryRoleDefinitionRepository();
        var adminRole = (await roles.GetByNameAsync("Admin"))!;
        await roles.SetPermissionsAsync(adminRole.Id, new[] { Permission.JobsView });
        var handler = new GetUserPermissionsHandler(
            new StubMembershipService(admin), new InMemoryGrantRepository(), roles);

        var view = await handler.HandleAsync(new GetUserPermissionsQuery(admin.Id));

        var jobsEdit = view.Permissions.Single(p => p.Permission == Permission.JobsEdit);
        Assert.False(jobsEdit.DefaultAllowed);
        Assert.False(jobsEdit.Effective);
        Assert.True(view.Permissions.Single(p => p.Permission == Permission.JobsView).DefaultAllowed);
    }

    [Fact]
    public async Task The_matrix_falls_back_to_the_hardcoded_defaults_when_no_role_row_exists()
    {
        // A role name with no role_definitions row and no UserRole enum entry resolves to an empty set.
        var weird = Member("NoSuchRole");
        var handler = new GetUserPermissionsHandler(
            new StubMembershipService(weird), new InMemoryGrantRepository(), new InMemoryRoleDefinitionRepository());

        var view = await handler.HandleAsync(new GetUserPermissionsQuery(weird.Id));

        Assert.All(view.Permissions, p => Assert.False(p.DefaultAllowed));
    }

    [Fact]
    public async Task Settings_manage_is_never_effective_for_a_non_default_admin_in_the_matrix()
    {
        // Admin's grant set includes settings.manage — the matrix still reports it ineffective for
        // anyone but the default admin.
        var admin = Member("Admin");
        var handler = new GetUserPermissionsHandler(
            new StubMembershipService(admin), new InMemoryGrantRepository(), new InMemoryRoleDefinitionRepository());

        var view = await handler.HandleAsync(new GetUserPermissionsQuery(admin.Id));

        var row = view.Permissions.Single(p => p.Permission == Permission.SettingsManage);
        Assert.True(row.DefaultAllowed);   // the role grants it…
        Assert.False(row.Effective);       // …but the default-admin gate strips it
    }

    private sealed class StubMembershipService : IMembershipService
    {
        private readonly List<MemberView> _members;
        public StubMembershipService(params MemberView[] members) => _members = members.ToList();

        public Task<IReadOnlyList<MemberView>> ListMembersAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MemberView>>(_members.ToList());
        public Task<MemberView?> GetMemberAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(_members.FirstOrDefault(m => m.Id == userId));
        public Task<bool> IsDefaultAdminAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(_members.FirstOrDefault(m => m.Id == userId)?.IsDefaultAdmin ?? false);
        public Task<string?> GetRoleAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(_members.FirstOrDefault(m => m.Id == userId)?.Role);

        public Task SetRoleAsync(Guid userId, string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteMemberAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<InviteView> CreateInviteAsync(string email, string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<InviteInfo?> GetInviteAsync(string token, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuthUser?> AcceptInviteAsync(string token, string displayName, string password, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class InMemoryGrantRepository : IUserPermissionGrantRepository
    {
        private readonly Dictionary<(Guid UserId, string Permission), bool> _grants = new();

        public Task<IReadOnlyDictionary<string, bool>> ListForUserAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, bool>>(
                _grants.Where(g => g.Key.UserId == userId).ToDictionary(g => g.Key.Permission, g => g.Value));

        public Task UpsertAsync(Guid userId, string permission, bool allowed, CancellationToken ct = default)
        {
            _grants[(userId, permission)] = allowed;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(Guid userId, string permission, CancellationToken ct = default)
        {
            _grants.Remove((userId, permission));
            return Task.CompletedTask;
        }
    }
}
