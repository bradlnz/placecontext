using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Shared;
using PlaceContext.Application.Tenancy;
using PlaceContext.Application.Triggers;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class CreateTriggerHandler : ICommandHandler<CreateTriggerCommand, TriggerView>
{
    private readonly IJobRepository _jobs;
    private readonly IJobChainRepository? _chains;
    private readonly ICurrentUser? _user;
    private readonly IJobTriggerRepository _triggers;
    private readonly ICronSchedule _cron;
    private readonly ICurrentTenant _tenant;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public CreateTriggerHandler(
        IJobRepository jobs, IJobTriggerRepository triggers, ICronSchedule cron,
        ICurrentTenant tenant, IUnitOfWork uow, IClock clock, IJobChainRepository? chains = null, ICurrentUser? user = null)
    {
        _chains = chains;
        _user = user;
        _jobs = jobs;
        _triggers = triggers;
        _cron = cron;
        _tenant = tenant;
        _uow = uow;
        _clock = clock;
    }

    public async Task<TriggerView> HandleAsync(CreateTriggerCommand command, CancellationToken ct = default)
    {
        if (!Enum.TryParse<TriggerKind>(command.Kind, ignoreCase: true, out var kind)
            || kind is not (TriggerKind.Schedule or TriggerKind.Event))
        {
            throw new ArgumentException($"Unknown trigger kind '{command.Kind}'. Expected 'Schedule' or 'Event'.");
        }
        var now = _clock.UtcNow;
        JobTrigger trigger;

        if (command.WorkflowId is { } workflowId)
        {
            if (kind != TriggerKind.Schedule || command.JobId is not null) throw new ArgumentException("Choose one workflow target for a schedule.");
            if (_user?.IsAuthenticated != true) throw new UnauthorizedAccessException("A schedule owner is required.");
            var workflow = _chains is null ? null : await _chains.GetByIdAsync(workflowId, ct);
            if (workflow is null) throw new ArgumentException("Workflow not found.");
            var cron = command.CronExpression?.Trim();
            if (string.IsNullOrWhiteSpace(cron) || !_cron.IsValid(cron)) throw new ArgumentException("A valid cron expression is required.");
            trigger = JobTrigger.CreateWorkflowSchedule(workflow.ProjectId, workflow.Id, _user.UserId, command.Name, cron,
                _cron.Next(cron, now, _tenant.TimeZoneId), now);
            await _triggers.AddAsync(trigger, ct);
            await _uow.SaveChangesAsync(ct);
            return TriggerViewMapper.ToView(trigger);
        }
        if (command.JobId is not { } jobId || jobId == Guid.Empty)
        {
            throw new ArgumentException($"A {kind} trigger requires a job.");
        }
        var job = await _jobs.GetByIdAsync(jobId, ct)
                    ?? throw new InvalidOperationException($"Job {jobId} not found.");
        JobTrigger CreateTrigger()
        {
            JobTrigger resolvedTrigger;
            if (kind == TriggerKind.Schedule)
            {
                var cron = command.CronExpression?.Trim()
                    ?? throw new ArgumentException("A schedule trigger requires a cron expression.");
                if (!_cron.IsValid(cron))
                {
                    throw new ArgumentException($"'{cron}' is not a valid cron expression.");
                }
                var next = _cron.Next(cron, now, _tenant.TimeZoneId);
                resolvedTrigger = JobTrigger.CreateSchedule(job.ProjectId, job.Id, command.Name, cron, next, now);
                return resolvedTrigger;
            }
            {
                var eventName = command.EventName?.Trim()
                    ?? throw new ArgumentException("An event trigger requires an event name.");
                resolvedTrigger = JobTrigger.CreateEvent(job.ProjectId, job.Id, command.Name, eventName, now);
                return resolvedTrigger;
            }
        }
        trigger = CreateTrigger();
        await _triggers.AddAsync(trigger, ct);
        await _uow.SaveChangesAsync(ct);
        return TriggerViewMapper.ToView(trigger);
    }
}
