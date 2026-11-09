using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Ask a project's knowledge graph a structured question (in-process, no LLM).</summary>
public sealed record QueryGraphQuery(Guid ProjectId, string Question) : IQuery<GraphQueryView>;
