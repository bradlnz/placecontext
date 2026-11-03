using System.Text.Json;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Organization;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;

namespace PlaceContext.Application.Tests;

public sealed class GroceryWorkflowManifestTests
{
    private sealed class ManifestFactAttribute : FactAttribute
    {
        public ManifestFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_GROCERY_MANIFEST")))
            {
                Skip = "Generate the grocery demo import and set PLACECONTEXT_TEST_GROCERY_MANIFEST to its path.";
            }
        }
    }

    [ManifestFact]
    public async Task Generated_pack_imports_actions_and_jobs_and_preserves_workspace_settings()
    {
        var manifest = JsonSerializer.Deserialize<BackupManifest>(
            await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_GROCERY_MANIFEST")!))!;
        var projects = new InMemoryProjectRepository();
        var jobs = new InMemoryJobRepository();
        var chains = new InMemoryJobChainRepository();
        var settings = new FakeTenantSettingsPort();
        var tenantId = Guid.NewGuid();
        var original = new TenantSettingsSnapshot("Australia/Brisbane", "{\"name\":\"Existing\"}", "existing-login");
        await settings.ApplyAsync(tenantId, original);
        var handler = new ImportManifestHandler(projects, jobs, chains, new InMemoryJobTriggerRepository(),
            new InMemoryEventRepository(), new InMemoryDataMappingRepository(), settings, new FakeCronSchedule(),
            new FakeCurrentTenant(tenantId), new RecordingUnitOfWork(), new FakeClock(DateTimeOffset.UtcNow));

        var imported = await handler.HandleAsync(new ImportManifestCommand(manifest));
        Assert.Empty(imported.Warnings);
        Assert.Equal(5, imported.JobsCreated);
        Assert.Equal(1, imported.JobChainsCreated);
        Assert.Equal(original, await settings.GetAsync(tenantId));
        var project = Assert.Single(await projects.ListAsync());
        var workflow = Assert.Single(await chains.ListForProjectAsync(project.Id.Value));
        Assert.Equal(8, workflow.Stages.Count);
        Assert.Collection(workflow.Stages.Where(stage => stage.Action is not null),
            stage => Assert.Equal("browser", Assert.IsType<VirtualMachineChainAction>(stage.Action).OutputField),
            stage => Assert.True(Assert.IsType<VirtualMachineChainAction>(stage.Action).AllowNetwork),
            stage => Assert.Equal("jev", Assert.IsType<LoopChainAction>(stage.Action).AgentProvider));
        var definitions = await jobs.ListForProjectAsync(project.Id.Value);
        Assert.DoesNotContain(definitions, job => job.AllowNetworkEgress);
        Assert.Contains(definitions, job => job.ReturnType == JobReturnType.Html);

        var repeated = await handler.HandleAsync(new ImportManifestCommand(manifest));
        Assert.Empty(repeated.Warnings);
        Assert.Equal(0, repeated.JobsCreated);
        Assert.Equal(1, repeated.JobChainsUpdated);
        Assert.Equal(5, (await jobs.ListForProjectAsync(project.Id.Value)).Count);
        Assert.Equal(original, await settings.GetAsync(tenantId));
    }
}
