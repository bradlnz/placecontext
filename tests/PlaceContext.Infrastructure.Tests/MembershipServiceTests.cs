using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Auth;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace PlaceContext.Infrastructure.Tests;

/// <summary>
/// Membership deletion and the default-admin role guards against a real (EF Core In-Memory)
/// <see cref="AppDbContext"/>, mirroring <see cref="AuthServiceTests"/>.
/// </summary>
public class MembershipServiceTests
{
    private static (MembershipService Service, AppDbContext Db) NewService(Guid? currentUserId = null)
    {
        var tenant = new FakeCurrentTenant(Guid.NewGuid());
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new AppDbContext(options, tenant);
        return (new MembershipService(db, new FakeCurrentUser(currentUserId ?? Guid.NewGuid()),
            new EfRoleDefinitionRepository(db)), db);
    }

    private static UserRow AddUser(
        AppDbContext db, string email, string role, bool isDefaultAdmin = false)
    {
        var row = new UserRow
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = email,
            PasswordHash = "x",
            PasswordSet = true,
            Role = role,
            IsDefaultAdmin = isDefaultAdmin,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(row);
        return row;
    }

    // ── DeleteMemberAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteMember_removes_the_user_and_their_permission_grants()
    {
        var (svc, db) = NewService();
        var target = AddUser(db, "member@example.com", "Member");
        db.UserPermissionGrants.Add(new UserPermissionGrantRow
        {
            Id = Guid.NewGuid(), UserId = target.Id, Permission = Permission.JobsEdit, Allowed = false,
        });
        var other = AddUser(db, "other@example.com", "Member");
        db.UserPermissionGrants.Add(new UserPermissionGrantRow
        {
            Id = Guid.NewGuid(), UserId = other.Id, Permission = Permission.JobsRun, Allowed = false,
        });
        await db.SaveChangesAsync();

        await svc.DeleteMemberAsync(target.Id);

        Assert.False(await db.Users.AnyAsync(u => u.Id == target.Id));
        Assert.False(await db.UserPermissionGrants.AnyAsync(g => g.UserId == target.Id));
        // Untouched: the other member and their grants.
        Assert.True(await db.Users.AnyAsync(u => u.Id == other.Id));
        Assert.True(await db.UserPermissionGrants.AnyAsync(g => g.UserId == other.Id));
    }

    [Fact]
    public async Task DeleteMember_refuses_the_default_admin()
    {
        var (svc, db) = NewService();
        var target = AddUser(db, "owner@example.com", "Owner", isDefaultAdmin: true);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DeleteMemberAsync(target.Id));
        Assert.True(await db.Users.AnyAsync(u => u.Id == target.Id));
    }

    [Fact]
    public async Task DeleteMember_refuses_an_owner()
    {
        var (svc, db) = NewService();
        var target = AddUser(db, "owner@example.com", "Owner");
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DeleteMemberAsync(target.Id));
    }

    [Fact]
    public async Task DeleteMember_refuses_the_current_user()
    {
        var self = Guid.NewGuid();
        var (svc, db) = NewService(currentUserId: self);
        AddUser(db, "me@example.com", "Admin").Id = self;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DeleteMemberAsync(self));
    }

    [Fact]
    public async Task DeleteMember_is_a_no_op_for_an_unknown_user()
    {
        var (svc, _) = NewService();

        await svc.DeleteMemberAsync(Guid.NewGuid()); // must not throw
    }

    // ── SetRoleAsync default-admin guard ─────────────────────────────────────────

    [Fact]
    public async Task SetRole_refuses_demoting_the_default_admin()
    {
        var (svc, db) = NewService();
        var target = AddUser(db, "owner@example.com", "Owner", isDefaultAdmin: true);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SetRoleAsync(target.Id, "Admin"));
    }

    [Fact]
    public async Task SetRole_still_changes_an_ordinary_members_role()
    {
        var (svc, db) = NewService();
        var target = AddUser(db, "member@example.com", "Member");
        await db.SaveChangesAsync();

        await svc.SetRoleAsync(target.Id, "Admin");

        Assert.Equal("Admin", (await db.Users.SingleAsync(u => u.Id == target.Id)).Role);
    }

    // ── String role names (custom roles) ─────────────────────────────────────────

    [Fact]
    public async Task SetRole_assigns_a_custom_role_from_role_definitions()
    {
        var (svc, db) = NewService();
        var target = AddUser(db, "member@example.com", "Member");
        db.RoleDefinitions.Add(new RoleDefinitionRow
        {
            Id = Guid.NewGuid(),
            Name = "Support",
            IsSystem = false,
            PermissionsJson = "[\"jobs.view\"]",
        });
        await db.SaveChangesAsync();

        await svc.SetRoleAsync(target.Id, "Support");

        Assert.Equal("Support", (await db.Users.SingleAsync(u => u.Id == target.Id)).Role);
    }

    [Fact]
    public async Task SetRole_rejects_an_unknown_role_name()
    {
        var (svc, db) = NewService();
        var target = AddUser(db, "member@example.com", "Member");
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => svc.SetRoleAsync(target.Id, "NoSuchRole"));
        Assert.Equal("Member", (await db.Users.SingleAsync(u => u.Id == target.Id)).Role);
    }

    [Fact]
    public async Task SetRole_rejects_owner_by_name()
    {
        var (svc, db) = NewService();
        var target = AddUser(db, "member@example.com", "Member");
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SetRoleAsync(target.Id, "Owner"));
    }

    [Fact]
    public async Task CreateInvite_accepts_a_custom_role_and_the_accepted_member_keeps_it()
    {
        var (svc, db) = NewService();
        db.RoleDefinitions.Add(new RoleDefinitionRow
        {
            Id = Guid.NewGuid(),
            Name = "Support",
            IsSystem = false,
            PermissionsJson = "[\"jobs.view\"]",
        });
        await db.SaveChangesAsync();

        var invite = await svc.CreateInviteAsync("new@example.com", "Support");
        Assert.Equal("Support", invite.Role);

        var user = await svc.AcceptInviteAsync(invite.Token, "New Person", "Zx7!qLmP4#vRw2");

        Assert.NotNull(user);
        Assert.Equal("Support", user!.Role);
        Assert.Equal("Support", (await db.Users.SingleAsync(u => u.Email == "new@example.com")).Role);
    }

    [Fact]
    public async Task CreateInvite_rejects_owner_and_unknown_role_names()
    {
        var (svc, _) = NewService();

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateInviteAsync("a@example.com", "Owner"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.CreateInviteAsync("a@example.com", "NoSuchRole"));
    }

    // ── ListMembersAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ListMembers_exposes_the_default_admin_flag()
    {
        var (svc, db) = NewService();
        AddUser(db, "owner@example.com", "Owner", isDefaultAdmin: true);
        AddUser(db, "member@example.com", "Member");
        await db.SaveChangesAsync();

        var members = await svc.ListMembersAsync();

        Assert.True(members.Single(m => m.Email == "owner@example.com").IsDefaultAdmin);
        Assert.False(members.Single(m => m.Email == "member@example.com").IsDefaultAdmin);
    }

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public FakeCurrentUser(Guid userId) => UserId = userId;
        public Guid UserId { get; }
        public string Role => "Admin";
        public bool IsAuthenticated => true;
    }
}
