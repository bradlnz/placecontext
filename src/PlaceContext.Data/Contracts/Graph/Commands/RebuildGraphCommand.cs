using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Data.Contracts.Graph;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>(Re)build a project's knowledge graph from logged activity and record the snapshot.</summary>
public sealed record RebuildGraphCommand(Guid ProjectId, bool Incremental = true) : ICommand<GraphRebuildResult>;
