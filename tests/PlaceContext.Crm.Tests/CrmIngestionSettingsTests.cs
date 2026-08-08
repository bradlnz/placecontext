using Microsoft.EntityFrameworkCore;
using PlaceContext.Crm.Infrastructure.Crm;
using PlaceContext.Crm.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.TestSupport;

namespace PlaceContext.Crm.Tests;

public sealed class CrmIngestionSettingsTests
{
    [Fact]
    public async Task Rotated_token_is_returned_once_and_only_its_hash_is_stored()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new FakeCurrentTenant(tenantId);
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        await using var db = new AppDbContext(options, tenant);
        var crmOptions = new DbContextOptionsBuilder<CrmDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        await using var crmDb = new CrmDbContext(crmOptions, tenant);
        await db.Tenants.AddAsync(new TenantRow
        {
            Id = tenantId, Slug = "example", Name = "Example", TimeZoneId = "Australia/Brisbane"
        });
        var project = new ProjectRow
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Name = "CRM", Path = "/crm", Status = "Active"
        };
        await db.Projects.AddAsync(project);
        await db.SaveChangesAsync();
        var service = new CrmIngestionSettingsService(crmDb, db, tenant);

        var rotated = await service.RotateAsync(project.Id, "https://forms.example.com/");

        Assert.StartsWith("pc_crm_", rotated.Token);
        Assert.Equal("https://forms.example.com", rotated.Settings.AllowedOrigin);
        var row = await crmDb.CrmIngestionSettings.SingleAsync();
        Assert.DoesNotContain(rotated.Token, row.TokenHash!);
        Assert.NotEqual(rotated.Token, row.TokenHash);
        var resolved = await service.ResolveAsync(rotated.Token);
        Assert.NotNull(resolved);
        Assert.Equal(project.Id, resolved.ProjectId);
        Assert.Equal(tenantId, resolved.Tenant.Id);

        await service.DisableAsync(project.Id);
        Assert.Null(await service.ResolveAsync(rotated.Token));
    }

    [Theory]
    [InlineData("https://forms.example.com/path")]
    [InlineData("https://user@forms.example.com")]
    [InlineData("http://forms.example.com")]
    [InlineData("javascript:alert(1)")]
    public void Origin_normalization_rejects_non_origin_values(string value)
        => Assert.Throws<ArgumentException>(() =>
            CrmIngestionSettingsService.NormalizeOrigin(value));

    [Fact]
    public void Origin_normalization_allows_https_and_local_development()
    {
        Assert.Equal("https://forms.example.com",
            CrmIngestionSettingsService.NormalizeOrigin("https://forms.example.com/"));
        Assert.Equal("http://localhost:5173",
            CrmIngestionSettingsService.NormalizeOrigin("http://localhost:5173"));
    }
}
