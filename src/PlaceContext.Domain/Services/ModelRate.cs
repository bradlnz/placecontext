using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Services;

/// <summary>Per-million-token USD rates for a model family.</summary>
public readonly record struct ModelRate(decimal InputPerMillion, decimal OutputPerMillion);
