using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>(Re)build a project's knowledge graph from logged activity and record the snapshot.</summary>
public sealed record RebuildGraphCommand(Guid ProjectId, bool Incremental = true) : ICommand<ProjectSummaryView>;
