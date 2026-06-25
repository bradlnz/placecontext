using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class RecordUsageHandler : ICommandHandler<RecordUsageCommand, UsageEntryView>
{
    private readonly IProjectRepository _projects;
    private readonly IUsageRepository _usage;
    private readonly TokenCostCalculator _cost;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public RecordUsageHandler(
        IProjectRepository projects, IUsageRepository usage, TokenCostCalculator cost, IUnitOfWork uow, IClock clock)
    {
        _projects = projects;
        _usage = usage;
        _cost = cost;
        _uow = uow;
        _clock = clock;
    }

    public async Task<UsageEntryView> HandleAsync(RecordUsageCommand command, CancellationToken ct = default)
    {
        var projectId = ProjectId.From(command.ProjectId);
        _ = await _projects.GetByIdAsync(projectId, ct)
            ?? throw new InvalidOperationException($"Project {command.ProjectId} not found.");

        var usage = TokenUsage.From(command.Model, command.InputTokens, command.OutputTokens);
        var record = UsageRecord.Record(projectId, usage, command.Description, _clock.UtcNow);
        await _usage.AddAsync(record, ct);
        await _uow.SaveChangesAsync(ct);

        return ViewMapper.ToView(record, _cost.CostUsd(usage));
    }
}
