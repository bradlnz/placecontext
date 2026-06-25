namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// One observed risk signal: a stable <see cref="Code"/>, its <see cref="Severity"/>, the
/// dimension it belongs to, and human-readable <see cref="Evidence"/>. Immutable; produced by the
/// pure domain scorers and folded into a <see cref="RiskScore"/> by the calculator.
/// </summary>
public sealed record RiskSignal
{
    public string Code { get; }
    public RiskKind Kind { get; }
    public Severity Severity { get; }
    public string Evidence { get; }

    private RiskSignal(string code, RiskKind kind, Severity severity, string evidence)
    {
        Code = code;
        Kind = kind;
        Severity = severity;
        Evidence = evidence;
    }

    public static RiskSignal Of(string code, RiskKind kind, Severity severity, string evidence)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("RiskSignal code must not be empty.", nameof(code));
        if (string.IsNullOrWhiteSpace(evidence))
            throw new ArgumentException("RiskSignal evidence must not be empty.", nameof(evidence));

        return new RiskSignal(code.Trim(), kind, severity, evidence.Trim());
    }

    public int Weight => (int)Severity;

    public override string ToString() => $"[{Kind}/{Severity}] {Code}: {Evidence}";
}
