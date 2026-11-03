using Microsoft.EntityFrameworkCore;
using Npgsql;
using PlaceContext.Application.DatabaseInspection;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Tests.AgentWorkflows;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests;

public sealed class DatabaseInspectorTests
{
    [Fact]
    public async Task Catalog_masks_unreviewed_fields_and_rejects_unknown_tables_before_opening_a_connection()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused").Options,
            new FakeCurrentTenant(Guid.NewGuid()));
        var inspector = new DatabaseInspector(db, new FakeCurrentTenant(Guid.NewGuid()));
        var tables = await inspector.ListTablesAsync();
        var users = Assert.Single(tables, table => table.Name == "public.users");
        Assert.True(users.CanReadRows);
        Assert.True(Assert.Single(users.Columns, column => column.Name == "PasswordHash").Redacted);
        Assert.False(Assert.Single(users.Columns, column => column.Name == "DisplayName").Redacted);
        Assert.Contains(tables, table => !table.CanReadRows);
        var global = tables.First(table => !table.CanReadRows);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => inspector.ReadTableAsync(global.Name, 0));
        await Assert.ThrowsAsync<ArgumentException>(() => inspector.ReadTableAsync("public.users; DROP TABLE users", 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => inspector.ReadTableAsync(users.Name, -1));
        var unresolved = new DatabaseInspector(db, new FakeCurrentTenant(Guid.Empty));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => unresolved.ListTablesAsync());
    }

    [AgentPostgresFact]
    public async Task Records_are_paginated_tenant_scoped_and_sensitive_values_never_leave_the_projection()
    {
        var schema = "database_inspection_" + Guid.NewGuid().ToString("N");
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_POSTGRES"))
        {
            SearchPath = schema
        };
        await using var connection = new NpgsqlConnection(settings.ConnectionString);
        await connection.OpenAsync();
        await using (var command = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection))
        {
            await command.ExecuteNonQueryAsync();
        }
        try
        {
            var tenant = new FakeCurrentTenant(Guid.NewGuid());
            var otherTenant = new FakeCurrentTenant(Guid.NewGuid());
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(settings.ConnectionString).Options;
            await using var db = new AppDbContext(options, tenant);
            await db.Database.MigrateAsync();
            db.Tenants.AddRange(
                new TenantRow { Id = tenant.TenantId, Slug = "inspection", Name = "Current workspace", GitHubToken = "private-token" },
                new TenantRow { Id = otherTenant.TenantId, Slug = "other", Name = "Other workspace" });
            for (var i = 0; i < 52; i++)
            {
                db.Users.Add(User(tenant.TenantId, $"User {i:00}"));
            }
            await db.SaveChangesAsync();
            await using (var otherDb = new AppDbContext(options, otherTenant))
            {
                otherDb.Users.Add(User(otherTenant.TenantId, "Other workspace user"));
                await otherDb.SaveChangesAsync();
            }
            var inspector = new DatabaseInspector(db, tenant);
            var first = await inspector.ReadTableAsync("public.users", 0);
            var second = await inspector.ReadTableAsync("public.users", DatabaseTablePage.PageSize);
            Assert.Equal(50, first.Rows.Count);
            Assert.Equal(2, second.Rows.Count);
            Assert.True(first.HasMore);
            Assert.False(second.HasMore);
            var columns = first.Table.Columns.Select(column => column.Name).ToList();
            var names = columns.IndexOf("DisplayName");
            var passwords = columns.IndexOf("PasswordHash");
            var ids = columns.IndexOf("Id");
            var owners = columns.IndexOf("TenantId");
            var rows = first.Rows.Concat(second.Rows).ToList();
            Assert.Equal(52, rows.Select(row => row[ids]).Distinct().Count());
            Assert.All(rows, row =>
            {
                Assert.StartsWith("User ", row[names]);
                Assert.Equal("[redacted]", row[passwords]);
                Assert.Equal(tenant.TenantId.ToString(), row[owners]);
                Assert.DoesNotContain("private-password-hash", row);
            });
            var tenants = await inspector.ReadTableAsync("public.tenants", 0);
            var ownTenant = Assert.Single(tenants.Rows);
            Assert.Contains("Current workspace", ownTenant);
            Assert.DoesNotContain("Other workspace", ownTenant);
            Assert.DoesNotContain("private-token", ownTenant);
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", connection);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private static UserRow User(Guid tenantId, string name) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Email = Guid.NewGuid().ToString("N") + "@example.test",
        DisplayName = name,
        PasswordHash = "private-password-hash",
        PasswordSet = true,
        Role = "Member",
        CreatedAt = DateTimeOffset.UtcNow
    };
}
