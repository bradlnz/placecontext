using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>
/// Record a change through the ledger: append → scoped git commit → attach the commit sha. The
/// heart of "the MCP manages all changes".
/// </summary>
public sealed record RecordActivityCommand(
    Guid ProjectId,
    string AuthorName,
    bool IsAgent,
    string? Rationale,
    IReadOnlyList<string> TouchedFiles,
    IReadOnlyList<string> TouchedNodes,
    int TestsAdded,
    int TestsRemoved,
    int TestsChanged,
    bool ArchitectureReviewerRun,
    bool LiveVerified,
    string CommitMessage) : ICommand<ActivityRecordView>;
