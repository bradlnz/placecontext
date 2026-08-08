using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlaceContext.Domain.ValueObjects;

/// <summary>Result of evaluating a gate against the current pipeline payload.</summary>
public sealed record GateResult(bool Proceed, TimeSpan? WaitDuration);
