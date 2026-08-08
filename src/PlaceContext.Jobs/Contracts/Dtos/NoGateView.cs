using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Dtos;

/// <summary>No gate — the stage runs unconditionally.</summary>
public sealed record NoGateView : ChainGateView;
