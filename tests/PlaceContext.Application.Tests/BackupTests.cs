using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

/// <summary>
/// Export→import round-trip for the backup/restore feature: a manifest built from one tenant's
/// repositories recreates the same jobs/chains/triggers/mappings when imported into a fresh set, and
/// re-importing the same manifest never duplicates anything (merge is idempotent).
/// </summary>
public class BackupTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    private sealed class Ctx
    {
        public InMemoryProjectRepository Projects = new();
        public InMemoryJobRepository Jobs = new();
        public InMemoryJobChainRepository Chains = new();
        public InMemoryJobTriggerRepository Triggers = new();
        public InMemoryEventRepository Events = new();
        public InMemoryDataMappingRepository Mappings = new();
        public FakeTenantSettingsPort TenantSettings = new();
        public FakeCronSchedule Cron = new();
        public FakeCurrentTenant Tenant = new(TenantId);
        public RecordingUnitOfWork Uow = new();
        public FakeClock Clock = new(T0);

        public ExportManifestHandler Exporter() =>
            new(Projects, Jobs, Chains, Triggers, Events, Mappings, TenantSettings, Tenant, Clock);

        public ImportManifestHandler Importer() =>
            new(Projects, Jobs, Chains, Triggers, Events, Mappings, TenantSettings, Cron, Tenant, Uow, Clock);
    }

    /// <summary>Seeds one project with one job, one chain (single step), one schedule trigger, one
    /// event type, and one data mapping — a representative slice of every exported entity kind.</summary>
    private static async Task<(Ctx Ctx, Project Project, Job Job)> SeedAsync()
    {
        var ctx = new Ctx();

        var project = Project.Discover(ProjectPath.From("/home/brad/code/alpha"), ProjectName.From("alpha"), T0);
        project.Register(T0);
        await ctx.Projects.AddAsync(project);

        var mapSpec = new MapSpec("img/worker:latest", new[] { "{}" }, new Dictionary<string, string>());
        var job = Job.Create(project.Id.Value, "nightly-report", "runs the nightly report", mapSpec, null,
            concurrencyLimit: 2, ExitCodePolicy.Default, T0, allowNetworkEgress: true,
            parameters: new[] { new JobParameter("customerEmail", "Customer email") },
            timeoutSeconds: 900, postJobActions: new[] { PostJobActionKind.Chart },
            returnType: JobReturnType.Json);
        await ctx.Jobs.AddAsync(job);

        var chain = JobChain.Create(project.Id.Value, "nightly-pipeline", "chains the report job", new[] { job.Id }, T0);
        await ctx.Chains.AddAsync(chain);

        var trigger = JobTrigger.CreateSchedule(project.Id.Value, job.Id, "nightly", "0 0 * * *", T0.AddHours(1), T0);
        await ctx.Triggers.AddAsync(trigger);

        var eventDef = EventDefinition.Create("deploy.finished", "after a deploy completes", null, T0);
        await ctx.Events.AddDefinitionAsync(eventDef);

        var fields = new[] { new DataFieldMapping("total", "total_amount", "numeric") };
        var mapping = DataMapping.Create(project.Id.Value, job.Id, "nightly_totals", null, fields, T0);
        await ctx.Mappings.AddAsync(mapping);

        await ctx.TenantSettings.ApplyAsync(TenantId, new TenantSettingsSnapshot("Australia/Brisbane", "{\"productName\":\"Acme\"}", "acme-bot"));

        return (ctx, project, job);
    }

    [Fact]
    public async Task Export_captures_every_entity_kind_and_tenant_settings()
    {
        var (ctx, project, job) = await SeedAsync();

        var manifest = await ctx.Exporter().HandleAsync(new ExportManifestQuery());

        Assert.Equal(BackupManifest.CurrentSchemaVersion, manifest.SchemaVersion);
        var pm = Assert.Single(manifest.Projects);
        Assert.Equal(project.Path.Value, pm.Path);

        var jm = Assert.Single(manifest.Jobs);
        Assert.Equal("nightly-report", jm.Name);
        Assert.Equal("image", jm.MapSourceKind);
        Assert.Equal("img/worker:latest", jm.MapImage);
        Assert.Equal(2, jm.ConcurrencyLimit);
        Assert.Equal(900, jm.TimeoutSeconds);
        Assert.True(jm.AllowNetworkEgress);
        Assert.Contains(PostJobActionKind.Chart, jm.PostJobActions);
        Assert.Single(jm.Parameters);

        var cm = Assert.Single(manifest.JobChains);
        Assert.Equal("nightly-pipeline", cm.Name);
        Assert.Equal(job.Id, Assert.Single(cm.StepJobIds));

        var tm = Assert.Single(manifest.Triggers);
        Assert.Equal("nightly", tm.Name);
        Assert.Equal("Schedule", tm.Kind);
        Assert.Equal("0 0 * * *", tm.CronExpression);

        var em = Assert.Single(manifest.EventDefinitions);
        Assert.Equal("deploy.finished", em.Name);

        var dm = Assert.Single(manifest.DataMappings);
        Assert.Equal("nightly_totals", dm.TargetTable);

        Assert.Equal("Australia/Brisbane", manifest.TenantSettings.TimeZoneId);
        Assert.Equal("acme-bot", manifest.TenantSettings.GitHubLogin);
        Assert.Contains("run history", manifest.Notes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Import_recreates_every_entity_into_a_fresh_workspace()
    {
        var (source, _, _) = await SeedAsync();
        var manifest = await source.Exporter().HandleAsync(new ExportManifestQuery());

        var target = new Ctx();
        var result = await target.Importer().HandleAsync(new ImportManifestCommand(manifest));

        Assert.Equal(1, result.ProjectsCreated);
        Assert.Equal(1, result.JobsCreated);
        Assert.Equal(1, result.JobChainsCreated);
        Assert.Equal(1, result.TriggersCreated);
        Assert.Equal(1, result.EventDefinitionsCreated);
        Assert.Equal(1, result.DataMappingsCreated);
        Assert.True(result.TenantSettingsApplied);
        Assert.Empty(result.Warnings);

        var project = Assert.Single(await target.Projects.ListAsync());
        var job = Assert.Single(await target.Jobs.ListForProjectAsync(project.Id.Value));
        Assert.Equal("nightly-report", job.Name);
        Assert.Equal(900, job.TimeoutSeconds);
        Assert.True(job.AllowNetworkEgress);

        var chain = Assert.Single(await target.Chains.ListForProjectAsync(project.Id.Value));
        Assert.Equal(job.Id, Assert.Single(chain.StepJobIds)); // rewired to the *new* job id, not the source's

        var trigger = Assert.Single(await target.Triggers.ListForProjectAsync(project.Id.Value));
        Assert.Equal(job.Id, trigger.JobId);
        Assert.NotNull(trigger.NextRunAt); // schedule triggers get a freshly-computed next fire time

        var mapping = Assert.Single(await target.Mappings.ListForProjectAsync(project.Id.Value));
        Assert.Equal(job.Id, mapping.JobId);

        var settings = await target.TenantSettings.GetAsync(TenantId);
        Assert.Equal("Australia/Brisbane", settings.TimeZoneId);
    }

    [Fact]
    public async Task Backup_round_trips_typed_email_action_and_stage_shape()
    {
        var (source, project, job) = await SeedAsync();
        var chain = Assert.Single(await source.Chains.ListForProjectAsync(project.Id.Value));
        chain.Update(chain.Name, chain.Description, new ChainStage[]
        {
            ChainStage.Of(job.Id),
            new(Array.Empty<Guid>(), new ConditionGate("eq:release.email_release:true"),
                action: new SendEmailChainAction(
                    "{{client.email}}", "{{client.name}}", "Your report", "Hello {{client.name}}")),
        }, T0.AddMinutes(1));

        var manifest = await source.Exporter().HandleAsync(new ExportManifestQuery());
        var exported = Assert.Single(manifest.JobChains);
        Assert.Equal(2, exported.Stages!.Count);
        Assert.Equal(SendEmailChainAction.ActionType, exported.Stages[1].Action!.Type);

        var target = new Ctx();
        await target.Importer().HandleAsync(new ImportManifestCommand(manifest));

        var importedProject = Assert.Single(await target.Projects.ListAsync());
        var imported = Assert.Single(await target.Chains.ListForProjectAsync(importedProject.Id.Value));
        Assert.Equal(2, imported.Stages.Count);
        var email = Assert.IsType<SendEmailChainAction>(imported.Stages[1].Action);
        Assert.Equal("{{client.email}}", email.Recipient);
        Assert.IsType<ConditionGate>(imported.Stages[1].Gate);
    }

    [Fact]
    public async Task Reimporting_the_same_manifest_is_idempotent()
    {
        var (source, _, _) = await SeedAsync();
        var manifest = await source.Exporter().HandleAsync(new ExportManifestQuery());

        var target = new Ctx();
        await target.Importer().HandleAsync(new ImportManifestCommand(manifest));
        var second = await target.Importer().HandleAsync(new ImportManifestCommand(manifest));

        // Everything already existed by natural key — the second pass updates in place, creates nothing.
        Assert.Equal(0, second.ProjectsCreated);
        Assert.Equal(0, second.JobsCreated);
        Assert.Equal(0, second.JobChainsCreated);
        Assert.Equal(0, second.TriggersCreated);
        Assert.Equal(0, second.EventDefinitionsCreated);
        Assert.Equal(0, second.DataMappingsCreated);

        Assert.Single(await target.Projects.ListAsync());
        var project = (await target.Projects.ListAsync()).Single();
        Assert.Single(await target.Jobs.ListForProjectAsync(project.Id.Value));
        Assert.Single(await target.Chains.ListForProjectAsync(project.Id.Value));
        Assert.Single(await target.Triggers.ListForProjectAsync(project.Id.Value));
        Assert.Single(await target.Mappings.ListForProjectAsync(project.Id.Value));
        Assert.Single(await target.Events.ListDefinitionsAsync());
    }

    [Fact]
    public async Task Import_skips_a_chain_step_referencing_an_unresolved_job_and_reports_it()
    {
        var manifest = new BackupManifest(
            BackupManifest.CurrentSchemaVersion, T0,
            new TenantSettingsManifest("UTC", null, null),
            Projects: new[] { new ProjectManifest(Guid.NewGuid(), "alpha", "/home/brad/code/alpha") },
            Jobs: Array.Empty<JobManifest>(), // no jobs at all
            JobChains: new[]
            {
                new JobChainManifest(Guid.NewGuid(), Guid.NewGuid() /* mismatched project id */, "orphan-chain", null,
                    new[] { Guid.NewGuid() /* unknown job */ }),
            },
            Triggers: Array.Empty<TriggerManifest>(),
            EventDefinitions: Array.Empty<EventDefinitionManifest>(),
            DataMappings: Array.Empty<DataMappingManifest>());

        var target = new Ctx();
        var result = await target.Importer().HandleAsync(new ImportManifestCommand(manifest));

        Assert.Equal(1, result.ProjectsCreated);
        Assert.Equal(0, result.JobChainsCreated);
        Assert.Equal(1, result.JobChainsSkipped);
        Assert.NotEmpty(result.Warnings);
    }
}
