using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Access;

/// <summary>Shared validation for role definitions: name shape and permission-catalog membership.
/// Pure — safe to unit-test in isolation.</summary>
public static class RoleDefinitionValidation
{
    public const int MaxNameLength = 64;

    /// <summary>Trims and validates a role name: non-empty, bounded, and a conservative charset
    /// (letters, digits, space, dash, underscore) so names stay safe in claims, JSON and URLs.</summary>
    public static string ValidateName(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new ArgumentException("Role name cannot be empty.");
        if (trimmed.Length > MaxNameLength)
            throw new ArgumentException($"Role name cannot exceed {MaxNameLength} characters.");
        if (trimmed.Any(c => !(char.IsLetterOrDigit(c) || c is ' ' or '-' or '_')))
            throw new ArgumentException("Role name may only contain letters, digits, spaces, dashes and underscores.");
        return trimmed;
    }

    /// <summary>Validates every permission against the catalog and returns them de-duplicated.</summary>
    public static IReadOnlyList<string> ValidatePermissions(IEnumerable<string>? permissions)
    {
        var result = (permissions ?? Enumerable.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        foreach (var permission in result)
            if (!Permission.All.Contains(permission))
                throw new ArgumentException($"Unknown permission '{permission}'.");
        return result;
    }
}
