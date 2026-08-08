using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Observability;

/// <summary>The most recent chain-run traces across the workspace, newest first — the Cluster/
/// Observability pages' chain lens.</summary>
public sealed record ListRecentChainRunTelemetryQuery(int Take = 50) : IQuery<IReadOnlyList<ChainRunTelemetry>>;
