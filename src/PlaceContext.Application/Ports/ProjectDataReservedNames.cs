namespace PlaceContext.Application.Ports;

/// <summary>
/// Path segments reserved under <c>/api/v1/{name}</c> for the management API and entity registry.
/// Project tables, views, and data-entity names (and their slugs) must not use these so the entity
/// data API can map <c>/api/v1/{entity-name}</c> unambiguously.
/// </summary>
public static class ProjectDataReservedNames
{
    /// <summary>Fixed first path segments under <c>/api/v1</c> that are never entity collections.</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "projects",
        "jobs",
        "schedules",
        "entities",
        "search",
    };

    /// <summary>True when <paramref name="name"/> collides with a reserved segment (raw, slug, or underscore form).</summary>
    public static bool IsReserved(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var n = name.Trim();
        if (All.Contains(n)) return true;

        var slug = Slug(n);
        if (slug.Length > 0 && All.Contains(slug)) return true;

        // Table/view idents use underscores; slugs use hyphens — treat both as the same token.
        var asTable = slug.Replace('-', '_');
        if (asTable.Length > 0 && All.Contains(asTable)) return true;

        var asSlugFromTable = n.Replace('_', '-');
        if (All.Contains(asSlugFromTable)) return true;

        return false;
    }

    /// <summary>Throws <see cref="ArgumentException"/> when <paramref name="name"/> is reserved.</summary>
    public static void EnsureAllowed(string name, string what = "name")
    {
        if (IsReserved(name))
            throw new ArgumentException(
                $"'{name.Trim()}' is reserved for the /api/v1 API ({string.Join(", ", All.Order())}). Choose a different {what}.");
    }

    /// <summary>
    /// URL slug form used by the entity data API: lower-case, non-alphanumerics → hyphens.
    /// Shared so table/entity validation and route matching agree.
    /// </summary>
    public static string Slug(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var chars = raw.Trim().Select(c =>
            char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c)
            : c is ' ' or '_' or '-' ? '-'
            : '\0').Where(c => c != '\0').ToArray();
        var s = new string(chars);
        while (s.Contains("--", StringComparison.Ordinal))
            s = s.Replace("--", "-", StringComparison.Ordinal);
        return s.Trim('-');
    }
}
