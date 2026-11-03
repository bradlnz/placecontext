using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Security;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests.Workload.TypeSafe;

internal sealed class ModelCredentialsVaultFixture
{
    public Guid Tenant { get; } = Guid.NewGuid();
    public Guid Project { get; } = Guid.NewGuid();
    public DataProtectionEncryptor Encryption { get; } = new(new EphemeralDataProtectionProvider());
    private readonly DbContextOptions<AppDbContext> _options = new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    public AppDbContext Db(Guid? tenant = null) => new(_options, new FakeCurrentTenant(tenant ?? Tenant));

    public async Task SeedAsync(Guid? tenant = null, Guid? project = null)
    {
        await using var db = Db(tenant);
        db.Projects.Add(new()
        {
            Id = project ?? Project,
            Name = "Vault test project"
        });
        await db.SaveChangesAsync();
    }
}
