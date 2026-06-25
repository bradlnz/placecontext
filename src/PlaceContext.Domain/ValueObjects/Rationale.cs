namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// Why a change was made. Optional: a change may carry <see cref="None"/>, but a *present*
/// rationale must be non-blank. Whether a rationale is absent is a first-class signal the
/// process-risk scorer reads.
/// </summary>
public sealed record Rationale
{
    /// <summary>Sentinel for "no rationale recorded" — drives an process-risk signal.</summary>
    public static readonly Rationale None = new(string.Empty);

    public string Value { get; }

    private Rationale(string value) => Value = value;

    public bool IsPresent => Value.Length > 0;

    public static Rationale Of(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A present Rationale must not be blank; use Rationale.None.", nameof(value));

        return new Rationale(value.Trim());
    }

    /// <summary>Maps a nullable/blank input to either a real rationale or <see cref="None"/>.</summary>
    public static Rationale OrNone(string? value)
        => string.IsNullOrWhiteSpace(value) ? None : Of(value);

    public override string ToString() => IsPresent ? Value : "(none)";
}
