using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Free-text search across workflows, jobs, agents, projects, change ledger, decisions, and
/// (when <paramref name="ProjectId"/> is supplied) that project's configured OpenSearch indexes.</summary>
public sealed record SearchQuery(string Term, int Limit = 25, Guid? ProjectId = null) : IQuery<SearchResultsView>;
