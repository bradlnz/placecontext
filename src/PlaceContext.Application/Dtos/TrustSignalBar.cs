namespace PlaceContext.Application.Dtos;

/// <summary>Read model: one bar in the process-trust signal histogram.</summary>
public sealed record TrustSignalBar(string Code, string Label, int Count, int Percent, string Tone);
