using System.Text.RegularExpressions;

namespace PlaceContext.Infrastructure.Caching;

/// <summary>Shared naming and creation rules for project chat channels.</summary>
public static partial class ChatChannel
{
    public const int MaxNameLength = 48;

    public static string NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var normalized = InvalidCharacters()
            .Replace(value.Trim().ToLowerInvariant(), "-")
            .Trim('-');
        if (normalized.Length > MaxNameLength)
            normalized = normalized[..MaxNameLength].TrimEnd('-');
        return normalized;
    }

    public static ChatSessionMemory Create(Guid projectId, string name, DateTimeOffset now)
    {
        var normalized = NormalizeName(name);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Enter a channel name using letters or numbers.", nameof(name));

        var id = Guid.NewGuid();
        return new ChatSessionMemory(id, projectId, normalized, [], now, now);
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex InvalidCharacters();
}
