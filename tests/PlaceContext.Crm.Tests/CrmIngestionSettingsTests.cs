using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Ports;
using PlaceContext.Crm.Infrastructure.Crm;
using PlaceContext.Crm.Infrastructure.Persistence;
using PlaceContext.Crm.Integration;
using PlaceContext.TestSupport;

namespace PlaceContext.Crm.Tests;

public sealed class CrmIngestionSettingsTests
{
    [Fact]
    public async Task Rotated_token_is_returned_once_and_only_its_hash_is_stored()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new FakeCurrentTenant(tenantId);
        var crmOptions = new DbContextOptionsBuilder<CrmDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var crmDb = new CrmDbContext(crmOptions, tenant);
        var projectId = Guid.NewGuid();
        var projects = new ProjectsClient(projectId);
        var tenants = new FakeTenantCatalog(
            new TenantContext(tenantId, "example", "Australia/Brisbane"));
        var service = new CrmIngestionSettingsService(crmDb, tenants, projects, tenant);

        var rotated = await service.RotateAsync(projectId, "https://forms.example.com/");

        Assert.StartsWith("pc_crm_", rotated.Token);
        Assert.Equal("https://forms.example.com", rotated.Settings.AllowedOrigin);
        var row = await crmDb.CrmIngestionSettings.SingleAsync();
        Assert.DoesNotContain(rotated.Token, row.TokenHash!);
        Assert.NotEqual(rotated.Token, row.TokenHash);
        var resolved = await service.ResolveAsync(rotated.Token);
        Assert.NotNull(resolved);
        Assert.Equal(projectId, resolved.ProjectId);
        Assert.Equal(tenantId, resolved.Tenant.Id);

        await service.DisableAsync(projectId);
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

    private sealed class ProjectsClient(params Guid[] projectIds) : ICrmProjectsClient
    {
        public Task<IReadOnlyList<CrmProjectSummary>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CrmProjectSummary>>([]);

        public Task<bool> ExistsAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult(projectIds.Contains(projectId));
    }
}
