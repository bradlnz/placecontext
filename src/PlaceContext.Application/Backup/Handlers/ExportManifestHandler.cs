using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class ExportManifestHandler : IQueryHandler<ExportManifestQuery, BackupManifest>
{
    private readonly IProjectRepository _projects;
    private readonly IJobRepository _jobs;
    private readonly IJobChainRepository _chains;
    private readonly IJobTriggerRepository _triggers;
    private readonly IEventRepository _events;
    private readonly IDataMappingRepository _mappings;
    private readonly ITenantSettingsPort _tenantSettings;
    private readonly ICurrentTenant _tenant;
    private readonly IClock _clock;

    public ExportManifestHandler(
        IProjectRepository projects, IJobRepository jobs, IJobChainRepository chains,
        IJobTriggerRepository triggers, IEventRepository events, IDataMappingRepository mappings,
        ITenantSettingsPort tenantSettings, ICurrentTenant tenant, IClock clock)
    {
        _projects = projects;
        _jobs = jobs;
        _chains = chains;
        _triggers = triggers;
        _events = events;
        _mappings = mappings;
        _tenantSettings = tenantSettings;
        _tenant = tenant;
        _clock = clock;
    }

    public async Task<BackupManifest> HandleAsync(ExportManifestQuery query, CancellationToken ct = default)
    {
        var projects = await _projects.ListAsync(ct);
        var projectManifests = projects.Select(p => new ProjectManifest(p.Id.Value, p.Name.Value, p.Path.Value)).ToList();

        var jobManifests = new List<JobManifest>();
        var chainManifests = new List<JobChainManifest>();
        var triggerManifests = new List<TriggerManifest>();
        var mappingManifests = new List<DataMappingManifest>();

        foreach (var project in projects)
        {
            var projectId = project.Id.Value;

            jobManifests.AddRange((await _jobs.ListForProjectAsync(projectId, ct)).Select(ToJobManifest));

            chainManifests.AddRange((await _chains.ListForProjectAsync(projectId, ct))
                .Select(ToChainManifest));

            triggerManifests.AddRange((await _triggers.ListForProjectAsync(projectId, ct))
                .Select(t => new TriggerManifest(t.Id, t.ProjectId, t.JobId, t.Name, t.Kind.ToString(), t.Enabled,
                    t.CronExpression, t.EventName, t.ChainId, t.SourceTable, t.Prompt)));

            mappingManifests.AddRange((await _mappings.ListForProjectAsync(projectId, ct))
                .Select(m => new DataMappingManifest(
                    m.Id, m.ProjectId, m.JobId, m.SourceKind, m.TargetTable, m.RowsPath,
                    m.Fields.Select(f => new DataFieldDto(f.SourcePath, f.Column, f.Type)).ToList(), m.Enabled)));
        }

        var eventManifests = (await _events.ListDefinitionsAsync(ct))
            .Select(d => new EventDefinitionManifest(d.Name, d.Description, d.PayloadSchema))
            .ToList();

        var settings = await _tenantSettings.GetAsync(_tenant.TenantId, ct);

        return new BackupManifest(
            BackupManifest.CurrentSchemaVersion,
            _clock.UtcNow,
            new TenantSettingsManifest(settings.TimeZoneId, settings.BrandingJson, settings.GitHubLogin),
            projectManifests, jobManifests, chainManifests, triggerManifests, eventManifests, mappingManifests);
    }

    // JobViewMapper.ToView already assembles the workload-source shape correctly (image vs. code,
    // multi-file vs. legacy single-source) — reuse it and add the one field the UI view doesn't carry.
    private static JobManifest ToJobManifest(Job job)
    {
        var v = JobViewMapper.ToView(job);
        return new JobManifest(
            v.Id, v.ProjectId, v.Name, v.Description,
            v.MapSourceKind, v.MapImage, v.MapRuntimeId, v.MapEntrypoint, v.MapFiles, v.InputPayloads, v.MapEnv,
            v.ReduceSourceKind, v.ReduceImage, v.ReduceRuntimeId, v.ReduceEntrypoint, v.ReduceFiles, v.ReduceEnv,
            v.ConcurrencyLimit, v.SuccessExitCodes, v.PartialExitCodes, v.AllowNetworkEgress, job.TimeoutSeconds,
            v.Parameters, v.PostJobActions, v.ReturnType, v.ReturnFileName,
            job.RetryCount, job.RetryDelaySeconds);
    }

    private static JobChainManifest ToChainManifest(JobChain chain) => new(
        chain.Id,
        chain.ProjectId,
        chain.Name,
        chain.Description,
        chain.StepJobIds.ToList(),
        chain.Stages.Select(stage => new JobChainStageManifest(
            stage.JobIds.ToList(),
            stage.Gate switch
            {
                WaitGate wait => new ChainGateManifest("wait", wait.Duration.TotalSeconds),
                ConditionGate condition => new ChainGateManifest("condition", Expression: condition.Expression),
                _ => null,
            },
            stage.Action switch
            {
                SendEmailChainAction email => new ChainActionManifest(
                    SendEmailChainAction.ActionType,
                    email.Recipient,
                    email.RecipientName,
                    email.Subject,
                    email.Body,
                    email.AttachmentPath),
                SendSmsChainAction sms => new ChainActionManifest(
                    SendSmsChainAction.ActionType,
                    sms.Recipient,
                    Body: sms.Body),
                _ => null,
            })).ToList());
}
