using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class CreateTriggerHandler : ICommandHandler<CreateTriggerCommand, TriggerView>
{
    private readonly IJobRepository _jobs;
    private readonly IJobChainRepository _chains;
    private readonly IJobTriggerRepository _triggers;
    private readonly ICronSchedule _cron;
    private readonly ICurrentTenant _tenant;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public CreateTriggerHandler(
        IJobRepository jobs, IJobChainRepository chains, IJobTriggerRepository triggers, ICronSchedule cron,
        ICurrentTenant tenant, IUnitOfWork uow, IClock clock)
    {
        _jobs = jobs;
        _chains = chains;
        _triggers = triggers;
        _cron = cron;
        _tenant = tenant;
        _uow = uow;
        _clock = clock;
    }

    public async Task<TriggerView> HandleAsync(CreateTriggerCommand command, CancellationToken ct = default)
    {
        if (!Enum.TryParse<TriggerKind>(command.Kind, ignoreCase: true, out var kind))
            throw new ArgumentException($"Unknown trigger kind '{command.Kind}'. Expected 'Schedule', 'Event', or 'Launchpad'.");

        var now = _clock.UtcNow;
        JobTrigger trigger;

        if (kind == TriggerKind.Launchpad)
        {
            var cron = command.CronExpression?.Trim()
                ?? throw new ArgumentException("A launchpad requires a cron expression.");
            if (!_cron.IsValid(cron))
                throw new ArgumentException($"'{cron}' is not a valid cron expression.");
            if (command.ChainId is not { } chainId || chainId == Guid.Empty)
                throw new ArgumentException("A launchpad requires a target job chain.");
            if (string.IsNullOrWhiteSpace(command.Prompt))
                throw new ArgumentException("A launchpad requires a prompt.");

            var chain = await _chains.GetByIdAsync(chainId, ct)
                ?? throw new InvalidOperationException($"Job chain {chainId} not found.");

            var next = _cron.Next(cron, now, _tenant.TimeZoneId);
            trigger = JobTrigger.CreateLaunchpad(
                chain.ProjectId, command.Name, cron, chainId, command.SourceTable, command.Prompt, next, now);
        }
        else
        {
            if (command.JobId is not { } jobId || jobId == Guid.Empty)
                throw new ArgumentException($"A {kind} trigger requires a job.");
            var job = await _jobs.GetByIdAsync(jobId, ct)
                ?? throw new InvalidOperationException($"Job {jobId} not found.");

            if (kind == TriggerKind.Schedule)
            {
                var cron = command.CronExpression?.Trim()
                    ?? throw new ArgumentException("A schedule trigger requires a cron expression.");
                if (!_cron.IsValid(cron))
                    throw new ArgumentException($"'{cron}' is not a valid cron expression.");

                var next = _cron.Next(cron, now, _tenant.TimeZoneId);
                trigger = JobTrigger.CreateSchedule(job.ProjectId, job.Id, command.Name, cron, next, now);
            }
            else
            {
                var eventName = command.EventName?.Trim()
                    ?? throw new ArgumentException("An event trigger requires an event name.");
                trigger = JobTrigger.CreateEvent(job.ProjectId, job.Id, command.Name, eventName, now);
            }
        }

        await _triggers.AddAsync(trigger, ct);
        await _uow.SaveChangesAsync(ct);
        return TriggerViewMapper.ToView(trigger);
    }
}
