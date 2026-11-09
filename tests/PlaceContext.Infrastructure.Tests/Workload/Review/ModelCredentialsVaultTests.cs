using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlaceContext.Infrastructure.Security;
using PlaceContext.Infrastructure.Workload;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests.Workload.TypeSafe;

public sealed class ModelCredentialsVaultTests
{
    [Fact]
    public async Task Review_and_Qemu_resolve_project_keys_without_mutating_shared_options()
    {
        var f = new ModelCredentialsVaultFixture();
        await f.SeedAsync();
        var secondProject = Guid.NewGuid();
        await f.SeedAsync(project: secondProject);
        var protector = new DataProtectionSecretProtector(f.Encryption);
        await using (var db = f.Db())
        {
            db.JobSecrets.Add(new()
            {
                ProjectId = f.Project,
                Name = "TYPESAFE_API_KEY",
                Cipher = protector.Protect("first-project-key")
            });
            db.JobSecrets.Add(new()
            {
                ProjectId = secondProject,
                Name = "TYPESAFE_API_KEY",
                Cipher = protector.Protect("second-project-key")
            });
            await db.SaveChangesAsync();
        }
        await using var provider = new ServiceCollection().AddScoped(_ => f.Db()).BuildServiceProvider();
        var options = Options.Create(new ModelReviewOptions());
        var resolver = new ModelCredentialsResolver(provider.GetRequiredService<IServiceScopeFactory>(), new FakeCurrentTenant(f.Tenant), protector, options);
        using var http = new ModelReviewHttp();
        var reviewer = new ModelOutcomeReviewer(http, options, resolver);
        await reviewer.EvaluateAsync("Is it valid?", "{}", default, f.Project);
        Assert.Equal("first-project-key", http.Authorization);
        await reviewer.EvaluateAsync("Is it valid?", "{}", default, secondProject);
        Assert.Equal("second-project-key", http.Authorization);
        Assert.Equal("", options.Value.ApiKey);
        var qemuOptions = Options.Create(new VirtualMachineEnvironmentOptions { Enabled = true, Agent = new() { Provider = "codex", Model = "test-model" } });
        var agent = new ReviewedVirtualMachineTaskAgent(http, qemuOptions, options, resolver);
        Assert.NotNull(agent.ConfigurationError);
        var runner = new VirtualMachineEnvironmentRunner(qemuOptions, agent);
        Assert.True((await runner.GetAsync(f.Project)).AgentReady);
        Assert.True((await runner.GetAsync(secondProject)).AgentReady);
        Assert.NotNull(agent.ConfigurationError);
    }

    [Fact]
    public async Task Foreign_projects_cannot_resolve_vault_credentials_and_host_fallback_stays_explicit()
    {
        var f = new ModelCredentialsVaultFixture();
        await f.SeedAsync();
        var otherTenant = Guid.NewGuid();
        var otherProject = Guid.NewGuid();
        await f.SeedAsync(otherTenant, otherProject);
        var protector = new DataProtectionSecretProtector(f.Encryption);
        await using var provider = new ServiceCollection().AddScoped(_ => f.Db()).BuildServiceProvider();
        var resolver = new ModelCredentialsResolver(provider.GetRequiredService<IServiceScopeFactory>(), new FakeCurrentTenant(f.Tenant), protector,
            Options.Create(new ModelReviewOptions { ApiKey = "host-key" }));
        Assert.Equal("host-key", (await resolver.ResolveAsync(f.Project)).ApiKey);
        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(otherProject));
        await using (var db = f.Db())
        {
            db.JobSecrets.Add(new()
            {
                ProjectId = f.Project,
                Name = "TYPESAFE_API_KEY",
                Cipher = protector.Protect("")
            });
            await db.SaveChangesAsync();
        }
        Assert.Equal("", (await resolver.ResolveAsync(f.Project)).ApiKey);
        using var http = new ModelReviewHttp();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ModelOutcomeReviewer(http, Options.Create(new ModelReviewOptions()), resolver)
            .EvaluateAsync("Is it valid?", "{}", default, f.Project));
        Assert.Null(http.Authorization);
    }
}
