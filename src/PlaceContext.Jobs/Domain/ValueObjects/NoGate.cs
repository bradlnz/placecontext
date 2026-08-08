using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlaceContext.Domain.ValueObjects;

/// <summary>No gate — the stage runs unconditionally (the default, backward compatible).</summary>
public sealed record NoGate : ChainGate
{
    public static readonly NoGate Instance = new();

    public override GateResult Evaluate(string? payload)
        => new(true, null);
}
