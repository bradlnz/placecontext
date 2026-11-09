using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>The most recent chain runs across every chain and project the tenant can see, newest
/// first — the cross-project chain history feed (Observability's "Chains" tab / Cluster's chain
/// tiles). Persisted, so — unlike the OTel-backed <c>ChainRunTelemetry</c> — it survives a restart
/// and isn't scoped to whichever replica happened to execute the run.</summary>
public sealed record ListRecentChainRunsQuery(int Take = 24) : IQuery<IReadOnlyList<ChainRunReportView>>;
