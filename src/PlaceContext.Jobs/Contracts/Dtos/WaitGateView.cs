using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Dtos;

/// <summary>Pauses the pipeline before the stage.</summary>
public sealed record WaitGateView(double DurationSeconds) : ChainGateView;
