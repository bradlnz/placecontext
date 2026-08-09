using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Operations.Contracts.Backup;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Operations.Backup;

/// <summary>
/// Merge-imports a <see cref="BackupManifest"/>: creates what's missing, updates what matches by
/// natural key, never duplicates. Cross-references (a job chain's steps, a trigger's job, a mapping's
/// source) are old ids from the manifest — they're rewired through old→new maps built as each entity
/// kind is resolved, so import order is projects → jobs → (chains, triggers, data mappings). A
/// reference that doesn't resolve is skipped and reported, not thrown — a partially-portable manifest
/// (e.g. hand-edited, or from a workspace with entities this tenant doesn't have) still imports what it
/// can.
/// </summary>
public sealed class ImportManifestHandler : ICommandHandler<ImportManifestCommand, ImportResultView>
{
    private readonly IProjectRepository _projects;
    private readonly IJobRepository _jobs;
    private readonly IJobChainRepository _chains;
    private readonly IJobTriggerRepository _triggers;
    private readonly IEventRepository _events;
    private readonly IDataMappingRepository _mappings;
    private readonly ITenantSettingsPort _tenantSettings;
    private readonly ICronSchedule _cron;
    private readonly ICurrentTenant _tenant;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public ImportManifestHandler(
        IProjectRepository projects, IJobRepository jobs, IJobChainRepository chains,
        IJobTriggerRepository triggers, IEventRepository events, IDataMappingRepository mappings,
        ITenantSettingsPort tenantSettings, ICronSchedule cron, ICurrentTenant tenant,
        IUnitOfWork uow, IClock clock)
    {
        _projects = projects;
        _jobs = jobs;
        _chains = chains;
        _triggers = triggers;
        _events = events;
        _mappings = mappings;
        _tenantSettings = tenantSettings;
        _cron = cron;
        _tenant = tenant;
        _uow = uow;
        _clock = clock;
    }

    public async Task<ImportResultView> HandleAsync(ImportManifestCommand command, CancellationToken ct = default)
    {
        if (command.Mode != ImportMode.Merge)
            throw new NotSupportedException($"Import mode '{command.Mode}' is not supported.");

        var m = command.Manifest;
        var now = _clock.UtcNow;
        var warnings = new List<string>();

        var (projectMap, projectsCreated, projectsUpdated) = await ImportProjectsAsync(m.Projects, now, ct);

        var (jobMap, jobsCreated, jobsUpdated, jobsSkipped) =
            await ImportJobsAsync(m.Jobs, projectMap, now, warnings, ct);

        var (chainMap, chainsCreated, chainsUpdated, chainsSkipped) =
            await ImportChainsAsync(m.JobChains, projectMap, jobMap, now, warnings, ct);

        var (triggersCreated, triggersUpdated, triggersSkipped) =
            await ImportTriggersAsync(m.Triggers, jobMap, now, warnings, ct);

        var (eventsCreated, eventsUpdated, eventsSkipped) =
            await ImportEventDefinitionsAsync(m.EventDefinitions, now, warnings, ct);

        var (mappingsCreated, mappingsUpdated, mappingsSkipped) =
            await ImportDataMappingsAsync(m.DataMappings, projectMap, jobMap, chainMap, now, warnings, ct);

        await _tenantSettings.ApplyAsync(_tenant.TenantId,
            new TenantSettingsSnapshot(m.TenantSettings.TimeZoneId, m.TenantSettings.BrandingJson, m.TenantSettings.GitHubLogin), ct);

        await _uow.SaveChangesAsync(ct);

        return new ImportResultView(
            projectsCreated, projectsUpdated,
            jobsCreated, jobsUpdated, jobsSkipped,
            chainsCreated, chainsUpdated, chainsSkipped,
            triggersCreated, triggersUpdated, triggersSkipped,
            eventsCreated, eventsUpdated, eventsSkipped,
            mappingsCreated, mappingsUpdated, mappingsSkipped,
            TenantSettingsApplied: true,
            Warnings: warnings);
    }

    // ── Projects: natural key = Path ─────────────────────────────────────────────────────────────

    private async Task<(Dictionary<Guid, Guid> Map, int Created, int Updated)> ImportProjectsAsync(
        IReadOnlyList<ProjectManifest> manifests, DateTimeOffset now, CancellationToken ct)
    {
        var map = new Dictionary<Guid, Guid>();
        int created = 0, updated = 0;

        foreach (var pm in manifests)
        {
            var path = ProjectPath.From(pm.Path);
            var existing = await _projects.GetByPathAsync(path, ct);
            if (existing is null)
            {
                var project = Project.Discover(path, ProjectName.From(pm.Name), now);
                project.Register(now);
                await _projects.AddAsync(project, ct);
                map[pm.ProjectId] = project.Id.Value;
                created++;
            }
            else
            {
                if (existing.Name.Value != pm.Name.Trim())
                {
                    existing.Rename(ProjectName.From(pm.Name));
                    await _projects.UpdateAsync(existing, ct);
                    updated++;
                }
                map[pm.ProjectId] = existing.Id.Value;
            }
        }

        return (map, created, updated);
    }

    // ── Jobs: natural key = (ProjectId, Name) ────────────────────────────────────────────────────

    private async Task<(Dictionary<Guid, Job> Map, int Created, int Updated, int Skipped)> ImportJobsAsync(
        IReadOnlyList<JobManifest> manifests, Dictionary<Guid, Guid> projectMap, DateTimeOffset now,
        List<string> warnings, CancellationToken ct)
    {
        var jobMap = new Dictionary<Guid, Job>();
        var byProject = new Dictionary<Guid, List<Job>>();
        int created = 0, updated = 0, skipped = 0;

        foreach (var jm in manifests)
        {
            if (!projectMap.TryGetValue(jm.ProjectId, out var newProjectId))
            {
                warnings.Add($"Job '{jm.Name}': references an unresolved project — skipped.");
                skipped++;
                continue;
            }

            WorkloadSource mapSource;
            try
            {
                mapSource = WorkloadSourceResolver.ResolveRequired(
                    jm.MapImage, jm.MapRuntimeId, source: null, jm.MapEntrypoint, jm.MapFiles, parameterContext: "Map");
            }
            catch (ArgumentException ex)
            {
                warnings.Add($"Job '{jm.Name}': {ex.Message} — skipped.");
                skipped++;
                continue;
            }

            var mapSpec = new MapSpec(mapSource, jm.InputPayloads, jm.MapEnv);
            ReduceSpec? reduceSpec = null;
            var reduceSource = WorkloadSourceResolver.Resolve(
                jm.ReduceImage, jm.ReduceRuntimeId, source: null, jm.ReduceEntrypoint, jm.ReduceFiles, parameterContext: "Reduce");
            if (reduceSource is not null)
                reduceSpec = new ReduceSpec(reduceSource, jm.ReduceEnv ?? new Dictionary<string, string>());

            var policy = new ExitCodePolicy(jm.SuccessExitCodes, jm.PartialExitCodes);
            var parameters = JobParameterMapper.ToDomain(jm.Parameters);

            if (!byProject.TryGetValue(newProjectId, out var siblings))
            {
                siblings = (await _jobs.ListForProjectAsync(newProjectId, ct)).ToList();
                byProject[newProjectId] = siblings;
            }

            var match = siblings.FirstOrDefault(j => string.Equals(j.Name, jm.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                match.Update(jm.Name, jm.Description, mapSpec, reduceSpec, jm.ConcurrencyLimit, policy, now,
                    jm.AllowNetworkEgress, parameters: parameters, timeoutSeconds: jm.TimeoutSeconds,
                    postJobActions: jm.PostJobActions, returnType: jm.ReturnType, returnFileName: jm.ReturnFileName,
                    retryCount: jm.RetryCount, retryDelaySeconds: jm.RetryDelaySeconds, allowApiInvocation: jm.AllowApiInvocation);
                await _jobs.UpdateAsync(match, ct);
                jobMap[jm.JobId] = match;
                updated++;
            }
            else
            {
                var job = Job.Create(newProjectId, jm.Name, jm.Description, mapSpec, reduceSpec, jm.ConcurrencyLimit, policy, now,
                    allowNetworkEgress: jm.AllowNetworkEgress, parameters: parameters, timeoutSeconds: jm.TimeoutSeconds,
                    postJobActions: jm.PostJobActions, returnType: jm.ReturnType, returnFileName: jm.ReturnFileName,
                    retryCount: jm.RetryCount, retryDelaySeconds: jm.RetryDelaySeconds, allowApiInvocation: jm.AllowApiInvocation);
                await _jobs.AddAsync(job, ct);
                siblings.Add(job);
                jobMap[jm.JobId] = job;
                created++;
            }
        }

        return (jobMap, created, updated, skipped);
    }

    // ── Job chains: natural key = (ProjectId, Name); steps rewired through the job map ──────────

    private async Task<(Dictionary<Guid, Guid> Map, int Created, int Updated, int Skipped)> ImportChainsAsync(
        IReadOnlyList<JobChainManifest> manifests, Dictionary<Guid, Guid> projectMap, Dictionary<Guid, Job> jobMap,
        DateTimeOffset now, List<string> warnings, CancellationToken ct)
    {
        var chainMap = new Dictionary<Guid, Guid>();
        int created = 0, updated = 0, skipped = 0;

        foreach (var cm in manifests)
        {
            if (!projectMap.TryGetValue(cm.ProjectId, out var newProjectId))
            {
                warnings.Add($"Chain '{cm.Name}': references an unresolved project — skipped.");
                skipped++;
                continue;
            }

            var stages = new List<ChainStage>();
            var dangling = false;
            if (cm.Stages is { Count: > 0 })
            {
                foreach (var stageManifest in cm.Stages)
                {
                    var jobs = new List<Guid>(stageManifest.JobIds.Count);
                    foreach (var oldJobId in stageManifest.JobIds)
                    {
                        if (jobMap.TryGetValue(oldJobId, out var job)) jobs.Add(job.Id);
                        else { dangling = true; break; }
                    }
                    if (dangling) break;
                    stages.Add(new ChainStage(
                        jobs,
                        ToGate(stageManifest.Gate),
                        action: ToAction(stageManifest.Action)));
                }
            }
            else
            {
                foreach (var oldJobId in cm.StepJobIds)
                {
                    if (jobMap.TryGetValue(oldJobId, out var job)) stages.Add(ChainStage.Of(job.Id));
                    else { dangling = true; break; }
                }
            }
            if (dangling)
            {
                warnings.Add($"Chain '{cm.Name}': a step references a job that couldn't be imported — skipped.");
                skipped++;
                continue;
            }

            var siblings = await _chains.ListForProjectAsync(newProjectId, ct);
            var match = siblings.FirstOrDefault(c => string.Equals(c.Name, cm.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                match.Update(cm.Name, cm.Description, stages, now);
                await _chains.UpdateAsync(match, ct);
                chainMap[cm.ChainId] = match.Id;
                updated++;
            }
            else
            {
                var chain = JobChain.Create(newProjectId, cm.Name, cm.Description, stages, now);
                await _chains.AddAsync(chain, ct);
                chainMap[cm.ChainId] = chain.Id;
                created++;
            }
        }

        return (chainMap, created, updated, skipped);
    }

    private static ChainGate? ToGate(ChainGateManifest? gate) => gate?.Type switch
    {
        "wait" => new WaitGate(TimeSpan.FromSeconds(gate.DurationSeconds ?? 0)),
        "condition" => new ConditionGate(gate.Expression ?? ""),
        _ => null,
    };

    private static ChainAction? ToAction(ChainActionManifest? action) => action?.Type switch
    {
        SendEmailChainAction.ActionType => new SendEmailChainAction(
            action.Recipient ?? "",
            action.RecipientName ?? "",
            action.Subject ?? "",
            action.Body ?? "",
            action.AttachmentPath ?? ""),
        SendSmsChainAction.ActionType => new SendSmsChainAction(
            action.Recipient ?? "",
            action.Body ?? ""),
        null => null,
        _ => throw new InvalidOperationException(
            $"Unsupported chain action '{action.Type}' in backup manifest."),
    };

    // ── Triggers: natural key = (JobId, Name) ───────────────────────────────────────────────────

    private async Task<(int Created, int Updated, int Skipped)> ImportTriggersAsync(
        IReadOnlyList<TriggerManifest> manifests, Dictionary<Guid, Job> jobMap, DateTimeOffset now,
        List<string> warnings, CancellationToken ct)
    {
        int created = 0, updated = 0, skipped = 0;

        foreach (var tm in manifests)
        {
            if (tm.Kind.Equals("Launchpad", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"Trigger '{tm.Name}': launchpad triggers are not importable yet — skipped.");
                skipped++;
                continue;
            }
            if (tm.JobId is not { } manifestJobId || !jobMap.TryGetValue(manifestJobId, out var job))
            {
                warnings.Add($"Trigger '{tm.Name}': references a job that couldn't be imported — skipped.");
                skipped++;
                continue;
            }
            if (!Enum.TryParse<TriggerKind>(tm.Kind, ignoreCase: true, out var kind))
            {
                warnings.Add($"Trigger '{tm.Name}': unknown kind '{tm.Kind}' — skipped.");
                skipped++;
                continue;
            }

            var siblings = await _triggers.ListForJobAsync(job.Id, ct);
            var match = siblings.FirstOrDefault(t => string.Equals(t.Name, tm.Name.Trim(), StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                if (match.Kind != kind)
                {
                    // A trigger's kind is immutable once created — a manifest that flips schedule<->event
                    // for the same name can't be applied in place.
                    warnings.Add($"Trigger '{tm.Name}': kind changed from {match.Kind} to {tm.Kind} — kept existing.");
                    skipped++;
                    continue;
                }

                match.Rename(tm.Name, now);
                if (kind == TriggerKind.Schedule && !string.IsNullOrWhiteSpace(tm.CronExpression))
                {
                    var next = _cron.IsValid(tm.CronExpression) ? _cron.Next(tm.CronExpression, now, _tenant.TimeZoneId) : null;
                    match.Reschedule(tm.CronExpression, next, now);
                }
                if (tm.Enabled && !match.Enabled) match.Enable(match.NextRunAt, now);
                else if (!tm.Enabled && match.Enabled) match.Disable(now);

                await _triggers.UpdateAsync(match, ct);
                updated++;
            }
            else
            {
                JobTrigger trigger;
                if (kind == TriggerKind.Schedule)
                {
                    if (string.IsNullOrWhiteSpace(tm.CronExpression) || !_cron.IsValid(tm.CronExpression))
                    {
                        warnings.Add($"Trigger '{tm.Name}': missing or invalid cron expression — skipped.");
                        skipped++;
                        continue;
                    }
                    var next = _cron.Next(tm.CronExpression, now, _tenant.TimeZoneId);
                    trigger = JobTrigger.CreateSchedule(job.ProjectId, job.Id, tm.Name, tm.CronExpression, next, now);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(tm.EventName))
                    {
                        warnings.Add($"Trigger '{tm.Name}': missing event name — skipped.");
                        skipped++;
                        continue;
                    }
                    trigger = JobTrigger.CreateEvent(job.ProjectId, job.Id, tm.Name, tm.EventName, now);
                }

                if (!tm.Enabled) trigger.Disable(now);
                await _triggers.AddAsync(trigger, ct);
                created++;
            }
        }

        return (created, updated, skipped);
    }

    // ── Event definitions: natural key = Name ───────────────────────────────────────────────────

    private async Task<(int Created, int Updated, int Skipped)> ImportEventDefinitionsAsync(
        IReadOnlyList<EventDefinitionManifest> manifests, DateTimeOffset now, List<string> warnings, CancellationToken ct)
    {
        int created = 0, updated = 0, skipped = 0;

        foreach (var em in manifests)
        {
            var existing = await _events.FindDefinitionByNameAsync(em.Name, ct);
            if (existing is not null)
            {
                existing.Update(em.Description, em.PayloadSchema, now);
                await _events.UpdateDefinitionAsync(existing, ct);
                updated++;
                continue;
            }

            try
            {
                var definition = EventDefinition.Create(em.Name, em.Description, em.PayloadSchema, now);
                await _events.AddDefinitionAsync(definition, ct);
                created++;
            }
            catch (ArgumentException ex)
            {
                warnings.Add($"Event '{em.Name}': {ex.Message} — skipped.");
                skipped++;
            }
        }

        return (created, updated, skipped);
    }

    // ── Data mappings: natural key = (ProjectId, SourceKind, JobId/ChainId, TargetTable) ───────

    private async Task<(int Created, int Updated, int Skipped)> ImportDataMappingsAsync(
        IReadOnlyList<DataMappingManifest> manifests, Dictionary<Guid, Guid> projectMap,
        Dictionary<Guid, Job> jobMap, Dictionary<Guid, Guid> chainMap, DateTimeOffset now,
        List<string> warnings, CancellationToken ct)
    {
        int created = 0, updated = 0, skipped = 0;

        foreach (var dm in manifests)
        {
            if (!projectMap.TryGetValue(dm.ProjectId, out var newProjectId))
            {
                warnings.Add($"Data mapping '{dm.TargetTable}': references an unresolved project — skipped.");
                skipped++;
                continue;
            }

            Guid sourceId;
            if (dm.SourceKind == "chain")
            {
                if (!chainMap.TryGetValue(dm.JobId, out sourceId))
                {
                    warnings.Add($"Data mapping '{dm.TargetTable}': references a chain that couldn't be imported — skipped.");
                    skipped++;
                    continue;
                }
            }
            else
            {
                if (!jobMap.TryGetValue(dm.JobId, out var job))
                {
                    warnings.Add($"Data mapping '{dm.TargetTable}': references a job that couldn't be imported — skipped.");
                    skipped++;
                    continue;
                }
                sourceId = job.Id;
            }

            IReadOnlyList<DataFieldMapping> fields;
            try
            {
                fields = dm.Fields.Select(f => new DataFieldMapping(f.SourcePath, f.Column, f.Type).ThrowIfInvalid()).ToList();
            }
            catch (ArgumentException ex)
            {
                warnings.Add($"Data mapping '{dm.TargetTable}': {ex.Message} — skipped.");
                skipped++;
                continue;
            }

            var siblings = await _mappings.ListForProjectAsync(newProjectId, ct);
            var match = siblings.FirstOrDefault(mm =>
                mm.SourceKind == dm.SourceKind && mm.JobId == sourceId &&
                string.Equals(mm.TargetTable, dm.TargetTable.Trim(), StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                match.Update(dm.TargetTable, dm.RowsPath, fields, dm.Enabled, now);
                await _mappings.UpdateAsync(match, ct);
                updated++;
            }
            else
            {
                var mapping = DataMapping.Create(newProjectId, sourceId, dm.TargetTable, dm.RowsPath, fields, now, dm.Enabled, dm.SourceKind);
                await _mappings.AddAsync(mapping, ct);
                created++;
            }
        }

        return (created, updated, skipped);
    }
}
