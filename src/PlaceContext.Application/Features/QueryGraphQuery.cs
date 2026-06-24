using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>Ask a project's decision tree a structured question (in-process, no LLM).</summary>
public sealed record QueryGraphQuery(Guid ProjectId, string Question) : IQuery<GraphQueryView>;
