using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

/// <summary>Free-text search across the tenant's projects, change ledger, context, decisions, and
/// (when <paramref name="ProjectId"/> is supplied) that project's configured OpenSearch indexes.</summary>
public sealed record SearchQuery(string Term, int Limit = 25, Guid? ProjectId = null) : IQuery<SearchResultsView>;
