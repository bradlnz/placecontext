using System.Text.RegularExpressions;

namespace PlaceContext.Application.Agents.Services;

/// <summary>
/// Parses <c>[[tool:name|args]]</c> tool calls out of a model response. Same regex as the
/// interactive chat page (Chat.razor) so a model trained on that catalog behaves identically
/// in unattended launchpad runs.
/// </summary>
public static class AgentToolCallParser
{
    private const string Pattern = @"\[\[tool:(\w+)\|([^\]]*)\]\]";

    /// <summary>All tool calls in the response, in order of appearance. Empty when none.</summary>
    public static IReadOnlyList<(string Name, string Args)> Parse(string response)
    {
        var calls = new List<(string Name, string Args)>();
        if (string.IsNullOrEmpty(response))
            return calls;
        foreach (Match m in Regex.Matches(response, Pattern))
            calls.Add((m.Groups[1].Value, m.Groups[2].Value));
        return calls;
    }

    /// <summary>The response text with every tool-call marker removed (display text).</summary>
    public static string StripToolCalls(string response)
        => string.IsNullOrEmpty(response)
            ? ""
            : Regex.Replace(response, @"\[\[tool:[^\]]*\]\]", "").Trim();
}
