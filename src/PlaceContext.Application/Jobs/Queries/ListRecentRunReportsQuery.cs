using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>
/// The reporting feed: the most recent runs across every job the tenant can see, each with its
/// full artifact payloads so the portal can chart them in one global view.
/// </summary>
public sealed record ListRecentRunReportsQuery(int Take = 24) : IQuery<IReadOnlyList<RunReportView>>;
