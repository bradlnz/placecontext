using System.Text;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>
/// Suggest improvements for a project, derived in-process from logged activity: churn hotspots from
/// the knowledge graph, unverified agent changes from the ledger, missing/stale context, and the latest
/// risk signals. Heuristic and deterministic — no LLM call.
/// </summary>
public sealed record SuggestImprovementsQuery(Guid ProjectId) : IQuery<ImprovementsView>;
