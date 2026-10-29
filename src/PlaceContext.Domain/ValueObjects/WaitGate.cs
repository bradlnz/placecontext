using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlaceContext.Domain.ValueObjects;

/// <summary>Pauses the pipeline for a given duration before the stage executes.</summary>
public sealed record WaitGate(TimeSpan Duration) : ChainGate
{
    public override GateResult Evaluate(string? payload)
        => new(true, Duration);
}
