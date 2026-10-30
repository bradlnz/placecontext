using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;
using PlaceContext.Infrastructure.AgentWorkflows;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Security;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests.AgentWorkflows;

internal sealed class AgentWorkflowFixture : IAsyncDisposable
{
    public Guid TenantId { get; } = Guid.NewGuid();
    public Guid ProjectId { get; } = Guid.NewGuid();
    public AgentTestModel Model { get; } = new();
    public AgentTestReviewer Reviewer { get; } = new();
    public AgentTestPermissions Permissions { get; } = new();
    public AgentWorkflowOptions Options { get; } = new() { SimulateSubscriptions = true };
    public HostingEnvironment Environment { get; } = new() { EnvironmentName = Environments.Development };
    public FakeClock Clock { get; } = new(DateTimeOffset.UtcNow);
    public DbContextOptions<AppDbContext> DbOptions { get; }
    private readonly List<ServiceProvider> _providers = [];
    private readonly DataProtectionEncryptor _encryption = new(new EphemeralDataProtectionProvider());

    public AgentWorkflowFixture(DbContextOptions<AppDbContext>? dbOptions = null) => DbOptions = dbOptions
        ?? new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
    public AppDbContext Db(Guid? tenantId = null) => new(DbOptions, new FakeCurrentTenant(tenantId ?? TenantId));
    public async Task SeedAsync()
    {
        await using var db = Db();
        db.Projects.Add(new() { Id = ProjectId, Name = "Agent workspace" });
        await db.SaveChangesAsync();
    }
    public AgentWorkflowService Service(Guid? tenantId = null)
    {
        var tenant = new FakeCurrentTenant(tenantId ?? TenantId);
        var provider = new ServiceCollection().AddScoped(_ => new AppDbContext(DbOptions, tenant)).BuildServiceProvider();
        _providers.Add(provider);
        return new(provider.GetRequiredService<IServiceScopeFactory>(), tenant, new AgentTestUser(), Permissions,
            Microsoft.Extensions.Options.Options.Create(Options), Environment, Model, Model, Reviewer, _encryption, Clock);
    }
    public async ValueTask DisposeAsync() { foreach (var provider in _providers) await provider.DisposeAsync(); }
}
