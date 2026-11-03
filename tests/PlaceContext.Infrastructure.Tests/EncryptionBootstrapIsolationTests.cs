using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Secrets;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Security;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests;

public sealed class EncryptionBootstrapIsolationTests
{
    [Fact]
    public async Task Legacy_encryption_runs_with_normal_tenant_guards_and_is_idempotent()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var owners = new[] { Guid.NewGuid(), Guid.NewGuid() };
        foreach (var owner in owners)
        {
            await using var db = new AppDbContext(options, new FakeCurrentTenant(owner));
            db.Requirements.Add(new()
            {
                ProjectId = Guid.NewGuid(),
                Markdown = "private requirements"
            });
            await db.SaveChangesAsync();
        }

        var encryptor = new DataProtectionEncryptor(new EphemeralDataProtectionProvider());
        await using var first = new AppDbContext(options, new FakeCurrentTenant(owners[0]));
        Assert.Equal(1, await EncryptionAtRestBootstrap.EncryptTenantAsync(first, encryptor));
        Assert.Equal(0, await EncryptionAtRestBootstrap.EncryptTenantAsync(first, encryptor));
        var encrypted = (await first.Requirements.SingleAsync()).Markdown;
        Assert.True(encryptor.IsProtected(encrypted));
        Assert.Equal("private requirements", encryptor.Unprotect(encrypted, IDataEncryptor.Purpose.Requirements));

        await using var second = new AppDbContext(options, new FakeCurrentTenant(owners[1]));
        Assert.Equal("private requirements", (await second.Requirements.SingleAsync()).Markdown);
        Assert.Equal(1, await EncryptionAtRestBootstrap.EncryptTenantAsync(second, encryptor));
    }
}
