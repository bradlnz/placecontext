using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlaceContext.Domain.ValueObjects;

[JsonDerivedType(typeof(NoGate), "none")]
[JsonDerivedType(typeof(WaitGate), "wait")]
[JsonDerivedType(typeof(ConditionGate), "condition")]
public abstract record ChainGate
{
    /// <summary>Evaluates the gate against the current pipeline payload.</summary>
    public abstract GateResult Evaluate(string? payload);
}
