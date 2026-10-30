using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Dashboards;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Dashboards;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Security;
using PlaceContext.Infrastructure.Tests.AgentWorkflows;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests.Dashboards;

internal sealed class DashboardFixture : IAsyncDisposable
{
    public Guid Tenant { get; } = Guid.NewGuid();
    public Guid Project { get; } = Guid.NewGuid();
    public AgentTestUser User { get; } = new();
    public AgentTestPermissions Permissions { get; } = new();
    public DashboardTestReviewer Reviewer { get; } = new();
    public FakeClock Clock { get; } = new(DateTimeOffset.UtcNow);
    public DataProtectionEncryptor Encryption { get; } = new(new EphemeralDataProtectionProvider());
    public DbContextOptions<AppDbContext> Options { get; }
    public DashboardFixture(DbContextOptions<AppDbContext>? options = null) => Options = options
        ?? new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
    private readonly List<ServiceProvider> _providers = [];
    public AppDbContext Db(Guid? tenant = null) => new(Options, new FakeCurrentTenant(tenant ?? Tenant));
    public PersonalDashboardService Service(Guid? tenant = null, ICurrentUser? user = null)
    {
        var current = new FakeCurrentTenant(tenant ?? Tenant);
        var provider = new ServiceCollection().AddScoped(_ => new AppDbContext(Options, current)).BuildServiceProvider();
        _providers.Add(provider);
        return new(provider.GetRequiredService<IServiceScopeFactory>(), current, user ?? User, Permissions, Encryption, Reviewer, Clock);
    }
    public async Task<DashboardChart> SeedAsync(Guid? tenant = null, Guid? project = null)
    {
        await using var db = Db(tenant);
        var projectId = project ?? Project;
        if (!await db.Projects.AnyAsync(x => x.Id == projectId)) db.Projects.Add(new() { Id = projectId, Name = "Test project" });
        var chart = new DashboardChart(Guid.NewGuid(), "Orders", new ChartSpec("bar", "Orders", ["A", "B"], [new ChartSeries("Count", [20, 5])]), Clock.UtcNow);
        db.ProjectCharts.Add(new() { Id = chart.Id, ProjectId = projectId, TableName = "orders",
            Html = Encryption.Protect(chart.Spec.ToJson(), IDataEncryptor.Purpose.Chart), GeneratedAt = chart.GeneratedAt });
        await db.SaveChangesAsync(); return chart;
    }
    public async ValueTask DisposeAsync() { foreach (var provider in _providers) await provider.DisposeAsync(); }
}
