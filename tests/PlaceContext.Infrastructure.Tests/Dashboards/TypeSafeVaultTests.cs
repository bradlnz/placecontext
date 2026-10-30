using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Security;
using PlaceContext.Infrastructure.Workload;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests.Dashboards;

public sealed class TypeSafeVaultTests
{
    [Fact]
    public async Task Review_and_Qemu_resolve_project_keys_without_mutating_shared_options()
    {
        await using var f = new DashboardFixture(); await f.SeedAsync();
        var secondProject = Guid.NewGuid(); await f.SeedAsync(project: secondProject);
        var protector = new DataProtectionSecretProtector(f.Encryption);
        await using (var db = f.Db())
        {
            db.JobSecrets.Add(new() { ProjectId = f.Project, Name = "TYPESAFE_API_KEY", Cipher = protector.Protect("first-project-key") });
            db.JobSecrets.Add(new() { ProjectId = secondProject, Name = "TYPESAFE_API_KEY", Cipher = protector.Protect("second-project-key") });
            await db.SaveChangesAsync();
        }
        await using var provider = new ServiceCollection().AddScoped(_ => f.Db()).BuildServiceProvider();
        var options = Options.Create(new TypeSafeOptions());
        var resolver = new TypeSafeConfiguration(provider.GetRequiredService<IServiceScopeFactory>(), new FakeCurrentTenant(f.Tenant), protector, options);
        using var http = new VaultJevHttp();
        var reviewer = new JevReviewer(http, options, resolver);
        await reviewer.EvaluateAsync("Is it valid?", "{}", default, f.Project);
        Assert.Equal("first-project-key", http.Authorization);
        await reviewer.EvaluateAsync("Is it valid?", "{}", default, secondProject);
        Assert.Equal("second-project-key", http.Authorization);
        Assert.Equal("", options.Value.ApiKey);
        var qemuOptions = Options.Create(new QemuEnvironmentOptions { Enabled = true, Agent = new() { Provider = "codex", Model = "test-model" } });
        var agent = new JevQemuTaskAgent(http, qemuOptions, options, resolver);
        Assert.NotNull(agent.ConfigurationError);
        var runner = new QemuEnvironmentRunner(qemuOptions, agent);
        Assert.True((await runner.GetAsync(f.Project)).AgentReady);
        Assert.True((await runner.GetAsync(secondProject)).AgentReady);
        Assert.NotNull(agent.ConfigurationError);
    }

    [Fact]
    public async Task Foreign_projects_cannot_resolve_vault_credentials_and_host_fallback_stays_explicit()
    {
        await using var f = new DashboardFixture(); await f.SeedAsync();
        var otherTenant = Guid.NewGuid(); var otherProject = Guid.NewGuid(); await f.SeedAsync(otherTenant, otherProject);
        var protector = new DataProtectionSecretProtector(f.Encryption);
        await using var provider = new ServiceCollection().AddScoped(_ => f.Db()).BuildServiceProvider();
        var resolver = new TypeSafeConfiguration(provider.GetRequiredService<IServiceScopeFactory>(), new FakeCurrentTenant(f.Tenant), protector,
            Options.Create(new TypeSafeOptions { ApiKey = "host-key" }));
        Assert.Equal("host-key", (await resolver.ResolveAsync(f.Project)).ApiKey);
        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(otherProject));
        await using (var db = f.Db())
        {
            db.JobSecrets.Add(new() { ProjectId = f.Project, Name = "TYPESAFE_API_KEY", Cipher = protector.Protect("") });
            await db.SaveChangesAsync();
        }
        Assert.Equal("", (await resolver.ResolveAsync(f.Project)).ApiKey);
        using var http = new VaultJevHttp();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new JevReviewer(http, Options.Create(new TypeSafeOptions()), resolver)
            .EvaluateAsync("Is it valid?", "{}", default, f.Project));
        Assert.Null(http.Authorization);
    }
}
