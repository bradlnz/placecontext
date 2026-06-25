namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// Absolute, normalized filesystem path to a project's working tree. Value Object: equality by
/// value, self-validating. Existence is a runtime concern checked by Infrastructure, not here —
/// the Domain only guarantees the path is absolute and normalized (no trailing separator).
/// </summary>
public sealed record ProjectPath
{
    public string Value { get; }

    private ProjectPath(string value) => Value = value;

    public static ProjectPath From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("ProjectPath must not be empty.", nameof(value));

        var normalized = value.Trim().Replace('\\', '/').TrimEnd('/');

        if (!normalized.StartsWith('/') && !IsWindowsRooted(normalized))
            throw new ArgumentException($"ProjectPath must be absolute: '{value}'.", nameof(value));

        return new ProjectPath(normalized);
    }

    /// <summary>The final path segment — the conventional default project name.</summary>
    public string LeafName => Value[(Value.LastIndexOf('/') + 1)..];

    private static bool IsWindowsRooted(string p)
        => p.Length >= 2 && char.IsLetter(p[0]) && p[1] == ':';

    public override string ToString() => Value;
}
