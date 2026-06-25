using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class RecordActivityHandler : ICommandHandler<RecordActivityCommand, ActivityRecordView>
{
    private readonly IProjectRepository _projects;
    private readonly IActivityLogRepository _ledgers;
    private readonly IGitPort _git;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public RecordActivityHandler(
        IProjectRepository projects, IActivityLogRepository ledgers,
        IGitPort git, IUnitOfWork uow, IClock clock)
    {
        _projects = projects;
        _ledgers = ledgers;
        _git = git;
        _uow = uow;
        _clock = clock;
    }

    public async Task<ActivityRecordView> HandleAsync(RecordActivityCommand command, CancellationToken ct = default)
    {
        var projectId = ProjectId.From(command.ProjectId);
        var project = await _projects.GetByIdAsync(projectId, ct)
            ?? throw new InvalidOperationException($"Project {command.ProjectId} not found.");

        var ledger = await _ledgers.GetForProjectAsync(projectId, ct);
        var author = Author.From(command.AuthorName, command.IsAgent ? ActorKind.Agent : ActorKind.Human);

        var record = ledger.Append(
            command.CommitMessage,
            author,
            Rationale.OrNone(command.Rationale),
            TestDelta.From(command.TestsAdded, command.TestsRemoved, command.TestsChanged),
            RiskDelta.From(command.RiskResolved, command.RiskIntroduced),
            new ActivityVerification(command.ArchitectureReviewerRun, command.LiveVerified),
            command.TouchedFiles,
            command.TouchedNodes.Select(GraphNodeId.From),
            _clock.UtcNow);

        // Scoped commit of exactly the touched files; embeds the change id for traceability.
        var message = $"{command.CommitMessage}\n\nPlaceContext-Change: {record.Id}";
        var sha = await _git.CommitScopedAsync(project.Path, command.TouchedFiles, message, author, ct);
        if (sha is not null)
            record.AttachCommit(sha);

        await _ledgers.SaveAsync(ledger, ct);
        await _uow.SaveChangesAsync(ct);
        return ViewMapper.ToView(record);
    }
}
