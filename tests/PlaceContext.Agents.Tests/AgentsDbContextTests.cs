using Microsoft.EntityFrameworkCore;
using PlaceContext.Agents.Domain.Entities;
using PlaceContext.Agents.Infrastructure.Persistence;
using PlaceContext.TestSupport;

namespace PlaceContext.Agents.Tests;

public sealed class AgentsDbContextTests
{
    [Fact]
    public async Task Tenant_owned_rows_are_stamped_and_filtered()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<AgentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AgentsDbContext(options, new FakeCurrentTenant(tenantId));
        db.Profiles.Add(ProfileRow(Guid.NewGuid(), Guid.Empty, "Current tenant"));
        db.Profiles.Add(ProfileRow(Guid.NewGuid(), otherTenantId, "Other tenant"));

        await db.SaveChangesAsync();

        Assert.Single(await db.Profiles.ToListAsync());
        Assert.Equal(tenantId, (await db.Profiles.SingleAsync()).TenantId);
        Assert.Equal(2, await db.Profiles.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Repository_round_trips_profiles_through_agents_persistence()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<AgentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AgentsDbContext(options, new FakeCurrentTenant(tenantId));
        var repository = new EfAgentsRepository(db);
        var profile = AgentProfile.Create(
            "Planner", "Researcher", "Plans work", "Research", "Use evidence.",
            "openai", "gpt-5", "high", ["search"], [], [], ["planning"],
            ["jobs.run"], true, true, true, 100_000, 20m, 90, 2, 3, 1,
            DateTimeOffset.UtcNow);

        await repository.AddProfileAsync(profile);
        await db.SaveChangesAsync();
        var loaded = await repository.GetProfileAsync(profile.Id);

        Assert.NotNull(loaded);
        Assert.Equal(profile.Name, loaded.Name);
        Assert.Equal(["search"], loaded.AllowedTools);
    }

    private static AgentProfileRow ProfileRow(Guid id, Guid tenantId, string name) => new()
    {
        Id = id,
        TenantId = tenantId,
        Name = name,
        Role = "Researcher",
        Provider = "openai",
        Model = "gpt-5",
        ReasoningLevel = "high",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };
}
