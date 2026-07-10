using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

/// <summary>Free-text search across the tenant's projects, change ledger, context, and decisions.</summary>
public sealed record SearchQuery(string Term, int Limit = 25) : IQuery<SearchResultsView>;
