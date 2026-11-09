using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PlaceContext.Application.Tenancy;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Security;
using PlaceContext.Infrastructure.Workload;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests;

public sealed class VirtualMachineLoginStoreTests
{
    private sealed record User(Guid UserId) : ICurrentUser
    {
        public string Role => "Owner"; public bool IsAuthenticated => true;
    }
    private sealed class Permissions(bool allowed) : IPermissionService
    {
        public Task<bool> HasAsync(string permission, CancellationToken ct = default) => Task.FromResult(allowed);
        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(Guid id, string role, CancellationToken ct = default) => throw new NotSupportedException();
    }
    [ChainInputResumeTests.PostgresFact]
    public async Task Logins_are_encrypted_private_to_the_owner_project_and_tenant_and_can_be_revoked()
    {
        var schema = "login_test_" + Guid.NewGuid().ToString("N");
        var cs = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_POSTGRES")) { SearchPath = schema }.ConnectionString;
        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        var tenant = new FakeCurrentTenant(Guid.NewGuid());
        var user = new User(Guid.NewGuid());
        var project = Guid.NewGuid();
        var otherProject = Guid.NewGuid();
        await using (var setup = new NpgsqlCommand($"CREATE SCHEMA {schema}; CREATE TABLE projects (\"Id\" uuid PRIMARY KEY, \"TenantId\" uuid NOT NULL);" + DbVirtualMachineLoginStore.Schema, connection))
        {
            await setup.ExecuteNonQueryAsync();
        }
        try
        {
            await using (var insert = new NpgsqlCommand("INSERT INTO projects VALUES (@project,@tenant),(@other,@tenant)", connection))
            {
                insert.Parameters.AddWithValue("project", project);
                insert.Parameters.AddWithValue("other", otherProject);
                insert.Parameters.AddWithValue("tenant", tenant.TenantId);
                await insert.ExecuteNonQueryAsync();
            }
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options;
            var encryption = new DataProtectionEncryptor(new EphemeralDataProtectionProvider());
            var store = new DbVirtualMachineLoginStore(options, tenant, user, new Permissions(true), encryption);
            var archive = VirtualMachineDesktopSessionTests.Archive();
            await store.SaveAsync(project, "Private staging", "browser", archive);
            var profile = Assert.Single(await store.ListAsync(project));
            Assert.Equal(archive, await store.ReadAsync(project, profile.Id, "browser"));
            await using (var raw = new NpgsqlCommand("SELECT \"Profile\" FROM qemu_login_profiles", connection))
            {
                var encrypted = (byte[])(await raw.ExecuteScalarAsync())!;
                Assert.False(archive.SequenceEqual(encrypted));
            }
            var otherUser = new DbVirtualMachineLoginStore(options, tenant, new User(Guid.NewGuid()), new Permissions(true), encryption);
            Assert.Empty(await otherUser.ListAsync(project));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => otherUser.ReadAsync(project, profile.Id, "browser"));
            await otherUser.DeleteAsync(project, profile.Id);
            Assert.Single(await store.ListAsync(project));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.ReadAsync(otherProject, profile.Id, "browser"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.ReadAsync(project, profile.Id, "other-image"));
            var otherTenant = new DbVirtualMachineLoginStore(options, new FakeCurrentTenant(Guid.NewGuid()), user, new Permissions(true), encryption);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => otherTenant.ListAsync(project));
            var denied = new DbVirtualMachineLoginStore(options, tenant, user, new Permissions(false), encryption);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => denied.ReadAsync(project, profile.Id, "browser"));
            await store.DeleteAsync(project, profile.Id);
            Assert.Empty(await store.ListAsync(project));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.ReadAsync(project, profile.Id, "browser"));
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", connection);
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
