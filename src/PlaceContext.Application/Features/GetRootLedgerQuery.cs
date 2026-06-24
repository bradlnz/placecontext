using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

// ---- Change Ledger (cross-project feed) ----

public sealed record GetRootLedgerQuery(int Take = 40) : IQuery<RootLedgerView>;
