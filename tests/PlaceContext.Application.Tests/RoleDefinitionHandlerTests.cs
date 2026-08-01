using PlaceContext.Application.Access;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

/// <summary>Handler tests for the editable-roles CRUD (ListRoles / CreateRole / UpdateRolePermissions /
/// DeleteRole) against in-memory stores.</summary>
public class RoleDefinitionHandlerTests
{
    private static MemberView Member(string role, bool isDefaultAdmin = false)
        => new(Guid.NewGuid(), $"{Guid.NewGuid():N}@example.com", "Member", role, isDefaultAdmin, DateTimeOffset.UtcNow);

    // ── ListRoles ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListRoles_returns_the_seeded_system_roles_with_member_counts()
    {
        var roles = new InMemoryRoleDefinitionRepository();
        var members = new StubMembershipService(Member("Admin"), Member("Member"), Member("Member"));
        var handler = new ListRolesHandler(roles, members);

        var result = await handler.HandleAsync(new ListRolesQuery());

        Assert.Equal(4, result.Count);
        Assert.All(result, r => Assert.True(r.IsSystem));
        Assert.Equal(1, result.Single(r => r.Name == "Admin").MemberCount);
        Assert.Equal(2, result.Single(r => r.Name == "Member").MemberCount);
        Assert.Equal(0, result.Single(r => r.Name == "Viewer").MemberCount);
        // Owner is seeded with the full catalog.
        Assert.Equal(
            Permission.All.OrderBy(p => p, StringComparer.Ordinal),
            result.Single(r => r.Name == "Owner").Permissions);
    }

    // ── CreateRole ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateRole_adds_a_non_system_role_with_the_given_permissions()
    {
        var roles = new InMemoryRoleDefinitionRepository();
        var handler = new CreateRoleHandler(roles, new RecordingUnitOfWork());

        var created = await handler.HandleAsync(
            new CreateRoleCommand("Support", new[] { Permission.JobsView, Permission.DataRead }));

        Assert.False(created.IsSystem);
        Assert.Equal(0, created.MemberCount);
        Assert.Equal(new[] { Permission.JobsView, Permission.DataRead }, created.Permissions);
        Assert.NotNull(await roles.GetByNameAsync("Support"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad/name")]
    [InlineData("bad;name")]
    public async Task CreateRole_refuses_an_invalid_name(string name)
    {
        var handler = new CreateRoleHandler(new InMemoryRoleDefinitionRepository(), new RecordingUnitOfWork());

        await Assert.ThrowsAsync<ArgumentException>(
            () => handler.HandleAsync(new CreateRoleCommand(name, new[] { Permission.JobsView })));
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("admin")] // case-insensitive
    public async Task CreateRole_refuses_a_system_role_name(string name)
    {
        var handler = new CreateRoleHandler(new InMemoryRoleDefinitionRepository(), new RecordingUnitOfWork());

        await Assert.ThrowsAsync<ArgumentException>(
            () => handler.HandleAsync(new CreateRoleCommand(name, new[] { Permission.JobsView })));
    }

    [Fact]
    public async Task CreateRole_refuses_a_duplicate_name()
    {
        var roles = new InMemoryRoleDefinitionRepository();
        var handler = new CreateRoleHandler(roles, new RecordingUnitOfWork());
        await handler.HandleAsync(new CreateRoleCommand("Support", new[] { Permission.JobsView }));

        await Assert.ThrowsAsync<ArgumentException>(
            () => handler.HandleAsync(new CreateRoleCommand("Support", new[] { Permission.DataRead })));
    }

    [Fact]
    public async Task CreateRole_refuses_a_permission_outside_the_catalog()
    {
        var handler = new CreateRoleHandler(new InMemoryRoleDefinitionRepository(), new RecordingUnitOfWork());

        await Assert.ThrowsAsync<ArgumentException>(
            () => handler.HandleAsync(new CreateRoleCommand("Support", new[] { "made.up" })));
    }

    // ── UpdateRolePermissions ────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateRolePermissions_replaces_a_system_roles_grant_set()
    {
        var roles = new InMemoryRoleDefinitionRepository();
        var admin = (await roles.GetByNameAsync("Admin"))!;
        var handler = new UpdateRolePermissionsHandler(roles, new StubMembershipService(), new RecordingUnitOfWork());

        var updated = await handler.HandleAsync(
            new UpdateRolePermissionsCommand(admin.Id, new[] { Permission.JobsView }));

        Assert.Equal(new[] { Permission.JobsView }, updated.Permissions);
        Assert.Equal(new[] { Permission.JobsView }, (await roles.GetByIdAsync(admin.Id))!.Permissions);
    }

    [Fact]
    public async Task UpdateRolePermissions_refuses_the_Owner_role()
    {
        var roles = new InMemoryRoleDefinitionRepository();
        var owner = (await roles.GetByNameAsync("Owner"))!;
        var handler = new UpdateRolePermissionsHandler(roles, new StubMembershipService(), new RecordingUnitOfWork());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(new UpdateRolePermissionsCommand(owner.Id, new[] { Permission.JobsView })));
    }

    // ── DeleteRole ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteRole_removes_an_unused_custom_role()
    {
        var roles = new InMemoryRoleDefinitionRepository();
        var created = await roles.CreateAsync("Support", new[] { Permission.JobsView });
        var handler = new DeleteRoleHandler(roles, new StubMembershipService(), new RecordingUnitOfWork());

        Assert.True(await handler.HandleAsync(new DeleteRoleCommand(created.Id)));
        Assert.Null(await roles.GetByIdAsync(created.Id));
    }

    [Fact]
    public async Task DeleteRole_refuses_a_system_role()
    {
        var roles = new InMemoryRoleDefinitionRepository();
        var viewer = (await roles.GetByNameAsync("Viewer"))!;
        var handler = new DeleteRoleHandler(roles, new StubMembershipService(), new RecordingUnitOfWork());

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(new DeleteRoleCommand(viewer.Id)));
    }

    [Fact]
    public async Task DeleteRole_refuses_a_role_still_assigned_to_a_member()
    {
        var roles = new InMemoryRoleDefinitionRepository();
        var created = await roles.CreateAsync("Support", new[] { Permission.JobsView });
        var handler = new DeleteRoleHandler(roles, new StubMembershipService(Member("Support")), new RecordingUnitOfWork());

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(new DeleteRoleCommand(created.Id)));
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
}
