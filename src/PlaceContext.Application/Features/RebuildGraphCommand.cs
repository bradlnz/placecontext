using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>(Re)build a project's decision tree from logged activity and record the snapshot.</summary>
public sealed record RebuildGraphCommand(Guid ProjectId, bool Incremental = true) : ICommand<ProjectSummaryView>;
