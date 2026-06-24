namespace PlaceContext.Application.Dtos;

/// <summary>Read model: one technical-debt metric card.</summary>
public sealed record TechMetricCard(string Label, string Value, string Unit, int Percent, string Tone);
