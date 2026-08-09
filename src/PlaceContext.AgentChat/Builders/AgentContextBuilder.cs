using System.Text.RegularExpressions;
using PlaceContext.AgentChat.Integration;

namespace PlaceContext.Application.Features;

public sealed class AgentContextBuilder(IAgentChatWorkspaceClient workspace)
{
    public Task<string> BuildContextAsync(
        Guid projectId,
        string userMessage,
        int maxChunks,
        CancellationToken ct = default)
        => workspace.BuildContextAsync(projectId, userMessage, maxChunks, ct);

    private static readonly Regex AddressPattern = new(
        @"\b\d+[A-Za-z]?\s+(?:[A-Z][A-Za-z0-9']+\s+){1,4}(?:Street|St|Road|Rd|Avenue|Ave|Drive|Dr|Lane|Ln|Parade|Pde|Court|Ct|Place|Pl|Terrace|Tce|Way|Circuit|Cct|Boulevard|Blvd|Crescent|Cres|Close|Highway|Hwy|Esplanade)\b",
        RegexOptions.Compiled);

    private static readonly Regex QuotedPattern = new(
        "\"([^\"]{3,80})\"|‘([^’]{3,80})’",
        RegexOptions.Compiled);

    internal static List<string> ExtractMentionTerms(string message)
    {
        var terms = new List<string>();
        if (string.IsNullOrWhiteSpace(message)) return terms;

        foreach (Match match in AddressPattern.Matches(message))
        {
            var term = match.Value.Trim();
            if (term.Length >= 8) terms.Add(term);
        }
        foreach (Match match in QuotedPattern.Matches(message))
        {
            var term = (match.Groups[1].Success
                ? match.Groups[1].Value
                : match.Groups[2].Value).Trim();
            if (term.Length >= 3) terms.Add(term);
        }

        return terms.Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList();
    }
}
