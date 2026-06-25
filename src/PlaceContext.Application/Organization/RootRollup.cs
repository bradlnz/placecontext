using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>Shared helpers for rolling Domain state up to root-level read models.</summary>
internal static class RootRollup
{
    public static string Band(double zeroToOne) => RiskScore.From(zeroToOne).Band.ToString();

    /// <summary>A project's context is "stale" when changes landed after its last graph build.</summary>
    public static bool IsStale(Project p, ActivityLog ledger)
        => p.LastGraph is not null && ledger.Records.Any(r => r.RecordedAt > p.LastGraph.BuiltAt);
}
