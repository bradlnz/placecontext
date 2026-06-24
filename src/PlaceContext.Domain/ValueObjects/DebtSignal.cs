namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// One observed debt signal: a stable <see cref="Code"/>, its <see cref="Severity"/>, the
/// dimension it belongs to, and human-readable <see cref="Evidence"/>. Immutable; produced by the
/// pure domain scorers and folded into a <see cref="DebtScore"/> by the calculator.
/// </summary>
public sealed record DebtSignal
{
    public string Code { get; }
    public DebtKind Kind { get; }
    public Severity Severity { get; }
    public string Evidence { get; }

    private DebtSignal(string code, DebtKind kind, Severity severity, string evidence)
    {
        Code = code;
        Kind = kind;
        Severity = severity;
        Evidence = evidence;
    }

    public static DebtSignal Of(string code, DebtKind kind, Severity severity, string evidence)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("DebtSignal code must not be empty.", nameof(code));
        if (string.IsNullOrWhiteSpace(evidence))
            throw new ArgumentException("DebtSignal evidence must not be empty.", nameof(evidence));

        return new DebtSignal(code.Trim(), kind, severity, evidence.Trim());
    }

    public int Weight => (int)Severity;

    public override string ToString() => $"[{Kind}/{Severity}] {Code}: {Evidence}";
}
