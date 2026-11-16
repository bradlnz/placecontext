using PlaceContext.Application.Dtos;
using PlaceContext.Application.Tenancy;
using PlaceContext.Application.Features;
using PlaceContext.Application.Networking;
using PlaceContext.Application.Organization;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using PlaceContext.Application.Access;

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
    private sealed class FakePermissionService(bool allowed) : IPermissionService
    {
        public Task<bool> HasAsync(string permission, CancellationToken ct = default) => Task.FromResult(allowed);
        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
        public Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(Guid id, string role, CancellationToken ct = default) => GetEffectivePermissionsAsync(ct);
    }

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

        public FakeWorkerNetworkService Networks = new();
        public ExportManifestHandler Exporter() =>
            new(Projects, Jobs, Chains, Triggers, Events, Mappings, TenantSettings, Tenant, Clock, Networks);

        public ImportManifestHandler Importer() =>
            new(Projects, Jobs, Chains, Triggers, Events, Mappings, TenantSettings, Cron, Tenant, Uow, Clock, Networks, new FakePermissionService(true));
    }

    [Fact]
    public async Task Backup_restores_workflow_schedule_for_the_importing_user()
    {
        var (source, project, job) = await SeedAsync();
        var workflow = JobChain.Create(project.Id.Value, "Scheduled workflow", null, [ChainStage.Of(job.Id)], T0);
        await source.Chains.AddAsync(workflow);
        var schedule = JobTrigger.CreateWorkflowSchedule(project.Id.Value, workflow.Id, Guid.NewGuid(), "Daily", "0 9 * * *", T0.AddDays(1), T0);
        schedule.Disable(T0); await source.Triggers.AddAsync(schedule);
        var manifest = await source.Exporter().HandleAsync(new ExportManifestQuery());
        var actor = new UserIdentity(Guid.NewGuid(), "Owner"); CurrentUser.Set(actor);
        try
        {
            var target = new Ctx();
            await target.Importer().HandleAsync(new ImportManifestCommand(manifest));
            var importedProject = Assert.Single(await target.Projects.ListAsync());
            var importedWorkflow = (await target.Chains.ListForProjectAsync(importedProject.Id.Value)).Single(chain => chain.Name == workflow.Name);
            var importedSchedule = (await target.Triggers.ListForProjectAsync(importedProject.Id.Value)).Single(trigger => trigger.Name == "Daily");
            Assert.Equal(importedWorkflow.Id, importedSchedule.WorkflowId);
            Assert.Equal(actor.Id, importedSchedule.ScheduledBy); Assert.False(importedSchedule.Enabled);
            await target.Importer().HandleAsync(new ImportManifestCommand(manifest));
            Assert.Single((await target.Triggers.ListForProjectAsync(importedProject.Id.Value)).Where(trigger => trigger.WorkflowId == importedWorkflow.Id));
        }
        finally { CurrentUser.Clear(); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Technical question?")]
    public async Task Backup_rewires_sub_workflows_in_both_routes_even_when_parent_appears_first(string? question)
    {
        var (source, project, job) = await SeedAsync();
        var child = JobChain.Create(project.Id.Value, "Specialist child", null, [ChainStage.Of(job.Id)], T0);
        await source.Chains.AddAsync(child);
        var parent = JobChain.Create(project.Id.Value, "Routing parent", null,
            [new ChainStage([], new ConditionGate("all:[\"true\",\"false\"]", [ChainStage.ForAction(new SubWorkflowChainAction(child.Id, child.Name))], question),
                action: new SubWorkflowChainAction(child.Id, child.Name), executionPolicy: new(2, 5, 90)),
             ChainStage.ForAction(new ParallelWorkflowsChainAction([new(child.Id, child.Name, "true"), new(child.Id, child.Name, "false")]))], T0);
        await source.Chains.AddAsync(parent);
        var manifest = await source.Exporter().HandleAsync(new ExportManifestQuery());
        manifest = manifest with { JobChains = manifest.JobChains.OrderByDescending(chain => chain.ChainId == parent.Id).ToArray() };
        var target = new Ctx();
        await target.Importer().HandleAsync(new ImportManifestCommand(manifest));
        var importedProject = Assert.Single(await target.Projects.ListAsync());
        var workflows = await target.Chains.ListForProjectAsync(importedProject.Id.Value);
        var restoredChild = workflows.Single(chain => chain.Name == child.Name);
        var restored = workflows.Single(chain => chain.Name == parent.Name).Stages[0];
        Assert.Equal(new StepExecutionPolicy(2, 5, 90), restored.ExecutionPolicy);
        Assert.NotEqual(child.Id, restoredChild.Id);
        Assert.Equal(restoredChild.Id, Assert.IsType<SubWorkflowChainAction>(restored.Action).WorkflowId);
        var gate = Assert.IsType<ConditionGate>(restored.Gate);
        Assert.Equal(restoredChild.Id, Assert.IsType<SubWorkflowChainAction>(Assert.Single(gate.ElseBranch!).Action).WorkflowId);
        Assert.Equal(question, gate.ReviewQuestion);
        if (question is null) Assert.False(gate.Evaluate("{}").Proceed);
        var parallel = Assert.IsType<ParallelWorkflowsChainAction>(workflows.Single(chain => chain.Name == parent.Name).Stages[1].Action);
        Assert.All(parallel.Branches, branch => Assert.Equal(restoredChild.Id, branch.WorkflowId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Network_backup_rewires_job_assignments_and_leaves_failed_restores_deny_egress(bool failAssignment)
    {
        var (source, project, job) = await SeedAsync();
        var group = new NetworkSecurityGroup(Guid.NewGuid(), "Web", [new("egress", "TCP", "10.20.0.0/24", 443, 443)]);
        var network = new WorkerNetwork(Guid.NewGuid(), project.Id.Value, "Workers", "10.20.0.0/24", [group]);
        await source.Networks.SaveAsync(network);
        await source.Networks.AssignAsync(project.Id.Value, job.Id, network.Id, group.Id);
        var manifest = await source.Exporter().HandleAsync(new ExportManifestQuery());
        Assert.Single(manifest.WorkerNetworks);
        Assert.Single(manifest.NetworkAssignments);
        var target = new Ctx();
        target.Networks.FailAssignment = failAssignment;
        async Task AssertImportAsync()
        {
            if (failAssignment)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => target.Importer().HandleAsync(new ImportManifestCommand(manifest)));
                return;
            }
            await target.Importer().HandleAsync(new ImportManifestCommand(manifest));
        }
        await AssertImportAsync();
        var targetProject = Assert.Single(await target.Projects.ListAsync());
        var targetJob = Assert.Single(await target.Jobs.ListForProjectAsync(targetProject.Id.Value));
        Assert.False(targetJob.AllowNetworkEgress);
        if (failAssignment)
        {
            Assert.Empty(target.Networks.Assignments);
            return;
        }
        {
            var restored = Assert.Single(target.Networks.Networks);
            Assert.NotEqual(network.Id, restored.Id);
            Assert.Equal(group.Rules, restored.Groups[0].Rules);
            Assert.Equal(targetJob.Id, Assert.Single(target.Networks.Assignments).JobId);
            await target.Importer().HandleAsync(new ImportManifestCommand(manifest));
            Assert.Single(target.Networks.Networks);
            Assert.Single(target.Networks.Assignments);
        }
    }

    /// <summary>Seeds one project with one job, one chain (single step), one schedule trigger, one
    /// event type, and one data mapping — a representative slice of every exported entity kind.</summary>
    private static async Task<(Ctx Ctx, Project Project, Job Job)> SeedAsync()
    {
        var ctx = new Ctx();

        var project = Project.Discover(ProjectPath.From("/workspace/alpha"), ProjectName.From("alpha"), T0);
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
    public async Task Backup_round_trips_typed_actions_and_stage_shape()
    {
        var (source, project, job) = await SeedAsync();
        var chain = Assert.Single(await source.Chains.ListForProjectAsync(project.Id.Value));
        chain.Update(chain.Name, chain.Description, new ChainStage[]
        {
            ChainStage.Of(job.Id),
            new(Array.Empty<Guid>(), new ConditionGate("eq:release.email_release:true"),
                action: new SendEmailChainAction(
                    "{{client.email}}", "{{client.name}}", "Your report", "Hello {{client.name}}")),
            ChainStage.ForAction(new LoopChainAction(LoopChainAction.HumanType, "Review the draft", "human_review", LoopChainAction.YesNoResponse)),
            ChainStage.ForAction(new LoopChainAction(LoopChainAction.AgentType, "Check the sources", "agent_review", agentProvider: LoopChainAction.JevAgent)),
            ChainStage.ForAction(new VirtualMachineChainAction("browser", "cat", "vm_result", 2048, 2, 600, true, loginProfileId: Guid.NewGuid())),
            ChainStage.ForAction(new LoopChainAction(LoopChainAction.TaskType, "Define requirements", "requirements")),
            ChainStage.ForAction(new QueryDataChainAction("SELECT id FROM listings LIMIT 10", "listings")),
            ChainStage.ForAction(new ScoringGateChainAction("review", "quality", [new ScoreCriterion("security", "Safe?", ["Fail", "Pass"], 2, 85)], 95, .9, false)),
            ChainStage.ForAction(new ContainerBuildChainAction("FROM alpine:3.21", "source.files", "built", 120, true)),
            ChainStage.ForAction(new ContainerRunChainAction(imageField: "built.image", command: ["serve"], port: 8080, memoryMb: 256)),
            ChainStage.ForAction(new RepeatFlowChainAction("eq:ready:true", 3)),
        }, T0.AddMinutes(1));

        var manifest = await source.Exporter().HandleAsync(new ExportManifestQuery());
        var exported = Assert.Single(manifest.JobChains);
        Assert.Equal(11, exported.Stages!.Count);
        Assert.Equal(SendEmailChainAction.ActionType, exported.Stages[1].Action!.Type);

        manifest = System.Text.Json.JsonSerializer.Deserialize<BackupManifest>(System.Text.Json.JsonSerializer.Serialize(manifest))!;
        var target = new Ctx();
        await target.Importer().HandleAsync(new ImportManifestCommand(manifest));

        var importedProject = Assert.Single(await target.Projects.ListAsync());
        var imported = Assert.Single(await target.Chains.ListForProjectAsync(importedProject.Id.Value));
        Assert.Equal(11, imported.Stages.Count);
        var email = Assert.IsType<SendEmailChainAction>(imported.Stages[1].Action);
        Assert.Equal("{{client.email}}", email.Recipient);
        Assert.IsType<ConditionGate>(imported.Stages[1].Gate);
        var human = Assert.IsType<LoopChainAction>(imported.Stages[2].Action);
        Assert.Equal(LoopChainAction.HumanType, human.Type);
        Assert.Equal("Review the draft", human.Instructions);
        Assert.Equal("human_review", human.ResponseField);
        Assert.Equal(LoopChainAction.YesNoResponse, human.ResponseType);
        var agent = Assert.IsType<LoopChainAction>(imported.Stages[3].Action);
        Assert.Equal(LoopChainAction.AgentType, agent.Type);
        Assert.Equal("Check the sources", agent.Instructions);
        Assert.Equal("agent_review", agent.ResponseField);
        Assert.Equal(LoopChainAction.JevAgent, agent.AgentProvider);
        var vm = Assert.IsType<VirtualMachineChainAction>(imported.Stages[4].Action);
        Assert.Equal("browser", vm.ImageProfile);
        Assert.Equal("cat", vm.Command);
        Assert.Equal("vm_result", vm.OutputField);
        Assert.Equal(2048, vm.MemoryMb);
        Assert.Equal(2, vm.Cpus);
        Assert.Equal(600, vm.TimeoutSeconds);
        Assert.True(vm.AllowNetwork);
        Assert.True(vm.PauseForLogin);
        Assert.Null(vm.LoginProfileId);
        Assert.Equal(LoopChainAction.TaskType, Assert.IsType<LoopChainAction>(imported.Stages[5].Action).Type);
        var query = Assert.IsType<QueryDataChainAction>(imported.Stages[6].Action);
        Assert.Equal("listings", query.OutputField); Assert.Equal("SELECT id FROM listings LIMIT 10", query.Sql);
        var scoring = Assert.IsType<ScoringGateChainAction>(imported.Stages[7].Action);
        Assert.Equal(95, scoring.PassScore); Assert.Equal(.9, scoring.MinimumConfidence); Assert.False(scoring.StopOnFailure);
        Assert.Equal(85, scoring.Criteria[0].MinimumScore);
        var repeat = Assert.IsType<RepeatFlowChainAction>(imported.Stages[10].Action);
        Assert.Equal("eq:ready:true", repeat.UntilCondition);
        Assert.Equal(3, repeat.MaxAttempts);
        var build = Assert.IsType<ContainerBuildChainAction>(imported.Stages[8].Action);
        Assert.Equal("source.files", build.ContextField); Assert.True(build.AllowNetwork);
        var container = Assert.IsType<ContainerRunChainAction>(imported.Stages[9].Action);
        Assert.Equal("built.image", container.ImageField); Assert.Equal(8080, container.Port); Assert.Equal(256, container.MemoryMb);
        Assert.Equal("serve", Assert.Single(container.Command));
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
        Assert.Single(await new ListEventTypesHandler(target.Events).HandleAsync(new ListEventTypesQuery()), e => !e.IsBuiltIn);
    }

    [Fact]
    public async Task Import_skips_a_chain_step_referencing_an_unresolved_job_and_reports_it()
    {
        var manifest = new BackupManifest(
            BackupManifest.CurrentSchemaVersion, T0,
            new TenantSettingsManifest("UTC", null, null),
            Projects: new[] { new ProjectManifest(Guid.NewGuid(), "alpha", "/workspace/alpha") },
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
