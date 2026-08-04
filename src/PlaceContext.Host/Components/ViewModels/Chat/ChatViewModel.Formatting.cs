using PlaceContext.Application;
using PlaceContext.Application.Agents;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Mcp;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Infrastructure.Caching;
using PlaceContext.Infrastructure.Chat;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class ChatViewModel
{
    // ── Content parsing (static, shared with ContentFormatter) ───────────────

    internal static List<ToolCallInfo> ParseToolCalls(string response)
    {
        var calls = new List<ToolCallInfo>();
        foreach (var c in ScanToolCalls(response))
            calls.Add(new ToolCallInfo { ToolName = c.Name, Args = c.Args });
        return calls;
    }

    internal static List<(string Name, string Args, int Start, int Length)> ScanToolCalls(
        string text
    )
    {
        var calls = new List<(string Name, string Args, int Start, int Length)>();
        if (string.IsNullOrEmpty(text))
            return calls;
        var pos = 0;
        while (pos < text.Length)
        {
            var start = text.IndexOf(AgentToolNames.ToolCallPrefix, pos, StringComparison.Ordinal);
            if (start < 0)
                break;
            var nameStart = start + AgentToolNames.ToolCallPrefix.Length;
            var pipe = text.IndexOf('|', nameStart);
            var nextCall = text.IndexOf(
                AgentToolNames.ToolCallPrefix,
                nameStart,
                StringComparison.Ordinal
            );
            if (pipe < 0 || (nextCall >= 0 && pipe > nextCall))
            {
                pos = nameStart;
                continue;
            }
            var searchEnd = nextCall >= 0 ? nextCall : text.Length;
            var close =
                searchEnd - pipe - 1 > 0
                    ? text.LastIndexOf(
                        AgentToolNames.ToolCallSuffix,
                        searchEnd - 1,
                        searchEnd - pipe - 1,
                        StringComparison.Ordinal
                    )
                    : -1;
            if (close < 0)
            {
                pos = nameStart;
                continue;
            }
            calls.Add((text[nameStart..pipe], text[(pipe + 1)..close], start, close + 2 - start));
            pos = close + 2;
        }
        return calls;
    }

    internal static string StripToolCallSyntax(string text)
    {
        var calls = ScanToolCalls(text);
        if (calls.Count == 0)
            return text;
        var sb = new System.Text.StringBuilder(text.Length);
        var pos = 0;
        foreach (var c in calls)
        {
            sb.Append(text, pos, c.Start - pos);
            pos = c.Start + c.Length;
        }
        sb.Append(text, pos, text.Length - pos);
        return sb.ToString();
    }

    public string FormatContent(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";
        if (raw.StartsWith("Called "))
        {
            var escaped = System.Net.WebUtility.HtmlEncode(raw);
            return $"<em>{escaped}</em>";
        }
        var attachIdx = raw.IndexOf("\n\n## Attached file:", StringComparison.Ordinal);
        if (attachIdx >= 0)
            raw = raw[..attachIdx];
        raw = StripThinkTags(raw);
        var cleaned = StripToolCallSyntax(raw).Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
            return "";
        var escaped2 = System.Net.WebUtility.HtmlEncode(cleaned);
        escaped2 = escaped2.Replace("\n", "<br/>");
        escaped2 = System.Text.RegularExpressions.Regex.Replace(
            escaped2,
            @"\*\*(.+?)\*\*",
            "<strong>$1</strong>"
        );
        escaped2 = System.Text.RegularExpressions.Regex.Replace(
            escaped2,
            @"`(.+?)`",
            "<code>$1</code>"
        );
        return escaped2;
    }

    internal static string StripThinkTags(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;
        var tags = new[] { "think", "reasoning", "reflection" };
        foreach (var tag in tags)
        {
            var open = "<" + tag;
            var close = "</" + tag + ">";
            var idx = text.IndexOf(open, StringComparison.OrdinalIgnoreCase);
            while (idx >= 0)
            {
                var closeIdx = text.IndexOf(close, idx, StringComparison.OrdinalIgnoreCase);
                if (closeIdx < 0)
                {
                    text = text[..idx];
                    break;
                }
                text = text[..idx] + text[(closeIdx + close.Length)..];
                idx = text.IndexOf(open, idx, StringComparison.OrdinalIgnoreCase);
            }
        }
        return text;
    }

    internal static string CleanAssistantOutput(string raw) => SplitThinking(raw).Answer;

    internal static (string Thinking, string Answer) SplitThinking(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return ("", raw ?? "");
        var thinking = new List<string>();
        var tagPattern =
            @"\x3c(?:think|reasoning|reflection)\b[^>]*>(.*?)(?:\x3c/(?:think|reasoning|reflection)\x3e|$)";
        raw = System.Text.RegularExpressions.Regex.Replace(
            raw,
            tagPattern,
            m =>
            {
                var inner = m.Groups[1].Value.Trim();
                if (inner.Length > 0)
                    thinking.Add(inner);
                return "";
            },
            System.Text.RegularExpressions.RegexOptions.Singleline
                | System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        raw = System.Text.RegularExpressions.Regex.Replace(
            raw,
            @"\x3c/(?:think|reasoning|reflection)\x3e",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        if (ScanToolCalls(raw).Count == 0 && IsAllReasoning(raw))
        {
            if (!string.IsNullOrWhiteSpace(raw))
                thinking.Add(raw.Trim());
            raw = "";
        }
        var lines = raw.Split('\n');
        var noiseStarts = new[]
        {
            "the user has provided",
            "looking at the conversation",
            "however, i notice",
            "let me re-read",
            "actually, i think",
            "let me think",
            "i notice there's",
            "there is no stream(gpu",
            "[cluster error:",
            "[error:",
            "thinking:",
            "reasoning:",
            "step-by-step:",
            "based on the",
            "i see that",
            "from the conversation",
            "the user is asking",
            "the user wants",
            "examining the",
            "reviewing the",
            "analyzing the",
            "hmm",
            "oh wait",
            "let me consider",
            "i should",
            "i need to",
            "wait, let me",
            "first, let me",
            "to answer this",
            "answering this question",
            "the context shows",
            "looking at this",
            "re-reading the",
            "now, i",
            "so, i",
            "next, i",
            "i need",
            "i will need",
            "looking at the tool",
            "the tool call",
            "calling the tool",
            "to display",
            "to show",
            "to render",
            "to fetch",
            "to get",
            "first, let me call",
            "let me call",
            "i'll call",
            "i should call",
            "the correct tool",
            "the right tool",
            "using the tool",
        };
        var cleaned = new List<string>();
        foreach (var l in lines)
        {
            if (
                noiseStarts.Any(p =>
                    l.TrimStart().StartsWith(p, StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                var t = l.Trim();
                if (t.Length > 0 && !t.StartsWith("[", StringComparison.Ordinal))
                    thinking.Add(t);
            }
            else
                cleaned.Add(l);
        }
        var result = string.Join("\n", cleaned).Trim();
        result = System.Text.RegularExpressions.Regex.Replace(
            result,
            @"^(answer|final answer)[:\-]\s*",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        return (string.Join("\n", thinking).Trim(), result);
    }

    internal static bool IsAllReasoning(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;
        var lines = content.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0)
            return false;
        foreach (var l in lines)
        {
            if (
                l.Contains('|')
                || l.Contains("http")
                || System.Text.RegularExpressions.Regex.IsMatch(l, @"\b\d{4,}\b")
            )
                return false;
        }
        var reasoningStarts = new[]
        {
            "i ",
            "i'll ",
            "let me ",
            "now ",
            "so ",
            "next ",
            "first ",
            "the user",
            "looking at",
            "based on",
            "from the",
            "to display",
            "to show",
            "to render",
            "to fetch",
            "to get",
            "calling",
            "the tool",
            "the correct",
            "the right",
            "hmm",
            "wait",
            "actually",
            "so, i",
            "now, i",
            "next, i",
            "i need",
            "i should",
            "i will",
            "i would",
            "i can",
            "i could",
            "to answer",
            "to summar",
            "this is",
            "that is",
            "here's",
            "one thing",
            "another",
            "however",
            "also",
        };
        var reasoningCount = lines.Count(l =>
            reasoningStarts.Any(p => l.StartsWith(p, StringComparison.OrdinalIgnoreCase))
        );
        return (double)reasoningCount / lines.Count > 0.7;
    }

    // ── Repetition detection ─────────────────────────────────────────────────

    internal static string NormalizeLineForRepetition(string line)
    {
        var l = line.Trim().ToLowerInvariant();
        l = System.Text.RegularExpressions.Regex.Replace(l, @"^(?:[-*•]|\d+[.)])\s*", "");
        l = System.Text.RegularExpressions.Regex.Replace(l, @"\s+", " ");
        return l;
    }

    internal static bool IsRepetitionLoopTail(string content)
    {
        if (string.IsNullOrEmpty(content))
            return false;
        var tail = content.Length > 4000 ? content[^4000..] : content;
        var significant = tail.Split('\n')
            .Select(NormalizeLineForRepetition)
            .Where(l => l.Length > 10)
            .ToList();
        var run = 1;
        for (var i = 1; i < significant.Count; i++)
        {
            run = significant[i] == significant[i - 1] ? run + 1 : 1;
            if (run >= 3 && i == significant.Count - 1)
                return true;
        }
        return false;
    }

    internal static string TruncateRepeatedLines(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return content;
        var lines = content.Split('\n');
        var significant = lines
            .Select((l, idx) => (norm: NormalizeLineForRepetition(l), idx))
            .Where(x => x.norm.Length > 10)
            .ToList();
        var run = 1;
        for (var k = 1; k < significant.Count; k++)
        {
            run = significant[k].norm == significant[k - 1].norm ? run + 1 : 1;
            if (run >= 3)
            {
                var firstOccurrenceIndex = significant[k - run + 1].idx;
                return string.Join("\n", lines.Take(firstOccurrenceIndex + 1)).TrimEnd();
            }
        }
        return content;
    }

    // ── Hallucination detection ──────────────────────────────────────────────

    private sealed class HallucinationResult
    {
        public bool Detected { get; init; }
        public string Reason { get; init; } = "";
        public string? ArtifactId { get; init; }
        public string? CorrectionPrompt { get; init; }
    }

    private HallucinationResult DetectHallucination()
    {
        var lastAssistant = Messages.LastOrDefault(m => m.Role == "assistant");
        if (lastAssistant == null)
            return new() { Detected = false };
        var content = StripToolCallSyntax(lastAssistant.Content).Trim();
        var allToolCalls = Messages
            .Where(m => m.Role == "assistant")
            .SelectMany(m => m.ToolCalls)
            .Where(tc => tc.Status == AgentToolCallStatus.Completed)
            .ToList();
        var toolResults = Messages
            .Where(m => m.Role == "system" && m.Content.Contains("Tool Results"))
            .Select(m => m.Content)
            .ToList();

        if (content.Length == 0 || FriendlyLoadingQuips.Contains(content))
            return new()
            {
                Detected = true,
                Reason = "Model produced no answer",
                CorrectionPrompt =
                    "You did not answer the user's question. Answer it directly now, or call a tool with [[tool:name|args]] if you need data. Do not output your reasoning — just the answer or the tool call.",
            };

        var genericResult = DetectGenericHallucination(content);
        if (genericResult.Detected)
            return genericResult;

        if (allToolCalls.Count > 0)
        {
            var artifactResult = DetectArtifactHallucination(allToolCalls, content);
            if (artifactResult.Detected)
                return artifactResult;
            var tableResult = DetectTableHallucination(allToolCalls, toolResults, content);
            if (tableResult.Detected)
                return tableResult;
            var searchResult = DetectSearchHallucination(allToolCalls, toolResults, content);
            if (searchResult.Detected)
                return searchResult;
            var runsResult = DetectRunsHallucination(allToolCalls, toolResults, content);
            if (runsResult.Detected)
                return runsResult;
            var jobsResult = DetectJobsHallucination(allToolCalls, toolResults, content);
            if (jobsResult.Detected)
                return jobsResult;
            var errorResult = DetectErrorMaskingHallucination(allToolCalls, content);
            if (errorResult.Detected)
                return errorResult;
            var emptyResult = DetectEmptyAfterTools(allToolCalls, content);
            if (emptyResult.Detected)
                return emptyResult;
        }
        var intentResult = DetectIntentMismatch(content);
        if (intentResult.Detected)
            return intentResult;
        return new() { Detected = false };
    }

    private static HallucinationResult DetectGenericHallucination(string content)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Length < 10)
            return new() { Detected = false };
        var lower = content.ToLowerInvariant();
        var words = lower.Split(
            new[] { ' ', '\t', '\n', '\r', ',', '.', ';', ':', '!', '?', '(', ')', '"', '\'' },
            StringSplitOptions.RemoveEmptyEntries
        );
        for (var i = 2; i < words.Length; i++)
        {
            if (words[i] == words[i - 1] && words[i - 1] == words[i - 2] && words[i].Length > 2)
                return new()
                {
                    Detected = true,
                    Reason = $"Word repetition: '{words[i]}'",
                    CorrectionPrompt =
                        "Your response contained repeated words. Please provide a clear, concise answer without repeating words or phrases.",
                };
        }
        {
            var significant = content
                .Split('\n')
                .Select(NormalizeLineForRepetition)
                .Where(l => l.Length > 10)
                .ToList();
            var run = 1;
            for (var i = 1; i < significant.Count; i++)
            {
                run = significant[i] == significant[i - 1] ? run + 1 : 1;
                if (run >= 3)
                    return new()
                    {
                        Detected = true,
                        Reason = $"Line repetition: {run}+ times",
                        CorrectionPrompt =
                            "Your response repeated the same line over and over. State each point once, then stop.",
                    };
            }
        }
        if (words.Length >= 6)
        {
            var phraseCounts = new Dictionary<string, int>();
            for (var i = 0; i <= words.Length - 3; i++)
            {
                var phrase = $"{words[i]} {words[i + 1]} {words[i + 2]}";
                phraseCounts.TryGetValue(phrase, out var count);
                phraseCounts[phrase] = count + 1;
            }
            var maxPhrase = phraseCounts.OrderByDescending(kv => kv.Value).FirstOrDefault();
            if (maxPhrase.Value >= 3)
                return new()
                {
                    Detected = true,
                    Reason = $"Phrase repetition: '{maxPhrase.Key}' x{maxPhrase.Value}",
                    CorrectionPrompt =
                        "Your response contained repeated phrases. Please provide a clear, concise answer without repeating the same phrases.",
                };
        }
        if (words.Length > 8)
        {
            var shortWords = words.Count(w => w.Length <= 2);
            if ((float)shortWords / words.Length > 0.6f)
                return new()
                {
                    Detected = true,
                    Reason = "Gibberish",
                    CorrectionPrompt =
                        "Your response was unclear. Please provide a meaningful answer.",
                };
        }
        return new() { Detected = false };
    }

    private static HallucinationResult DetectArtifactHallucination(
        List<ToolCallInfo> toolCalls,
        string content
    )
    {
        var getArtifactCalls = toolCalls
            .Where(tc => tc.ToolName == AgentToolNames.GetArtifacts)
            .ToList();
        if (
            getArtifactCalls.Count == 0
            || toolCalls.Any(tc => tc.ToolName == AgentToolNames.ShowArtifact)
        )
            return new() { Detected = false };
        var hasRealArtifacts = getArtifactCalls.Any(tc =>
            tc.Result != null
            && !tc.Result.StartsWith("No artifacts")
            && !tc.Result.StartsWith("No artifacts matched")
            && tc.Result.Contains("id:")
        );
        if (!hasRealArtifacts)
            return new() { Detected = false };
        return new()
        {
            Detected = true,
            Reason = "Agent did not call show_artifact after get_artifacts",
            CorrectionPrompt =
                "You found artifacts but did not fetch their content. You MUST call [[tool:show_artifact|id]] to get the actual content before summarizing. Pick the most relevant artifact and call show_artifact now.",
        };
    }

    private static HallucinationResult DetectEmptyAfterTools(
        List<ToolCallInfo> toolCalls,
        string content
    )
    {
        if (toolCalls.Count == 0)
            return new() { Detected = false };
        var successfulCalls = toolCalls
            .Where(tc => tc.Status == AgentToolCallStatus.Completed)
            .ToList();
        if (successfulCalls.Count == 0 || content.Length >= 40)
            return new() { Detected = false };
        return new()
        {
            Detected = true,
            Reason = $"Empty/short response after {successfulCalls.Count} successful tool call(s)",
            CorrectionPrompt =
                "You called tools but did not provide an answer. Use the tool results to give the user a direct, helpful response. Do not output your thinking process — just answer.",
        };
    }

    private static HallucinationResult DetectTableHallucination(
        List<ToolCallInfo> toolCalls,
        List<string> toolResults,
        string content
    )
    {
        var tableCalls = toolCalls.Where(tc => tc.ToolName == AgentToolNames.QueryTable).ToList();
        if (tableCalls.Count == 0)
            return new() { Detected = false };
        var allResultText = string.Join("\n", toolCalls.Select(tc => tc.Result ?? ""));
        var resultWords = ExtractMeaningfulWords(allResultText);
        if (resultWords.Count == 0)
            return new() { Detected = false };
        var quotedInResponse = System
            .Text.RegularExpressions.Regex.Matches(content, @"""([^""]{3,})""")
            .Select(m => m.Groups[1].Value.ToLowerInvariant())
            .Where(v =>
                !new[]
                {
                    "the",
                    "and",
                    "for",
                    "not",
                    "you",
                    "are",
                    "was",
                    "has",
                    "but",
                    "can",
                    "may",
                    "all",
                    "its",
                }.Contains(v)
            )
            .ToList();
        var fabricated = quotedInResponse.Where(q => !resultWords.Contains(q)).ToList();
        if (fabricated.Count >= 2)
            return new()
            {
                Detected = true,
                Reason = $"Fabricated values: {string.Join(", ", fabricated.Take(3))}",
                CorrectionPrompt =
                    "Your response contained values that were not in the table data. Only reference values that appear in the tool results.",
            };
        return new() { Detected = false };
    }

    private static HallucinationResult DetectSearchHallucination(
        List<ToolCallInfo> toolCalls,
        List<string> toolResults,
        string content
    )
    {
        var searchCalls = toolCalls.Where(tc => tc.ToolName == AgentToolNames.Search).ToList();
        if (searchCalls.Count == 0)
            return new() { Detected = false };
        var noMatch = searchCalls.Any(tc =>
            tc.Result != null && tc.Result.Contains("No matching run outputs found")
        );
        if (
            noMatch
            && content.Length > 40
            && (
                content.Contains("found")
                || content.Contains("match")
                || content.Contains("result")
                || content.Contains("showed")
            )
        )
            return new()
            {
                Detected = true,
                Reason = "Agent claimed search found results when search returned no matches",
                CorrectionPrompt =
                    "The search returned no matches. Do not fabricate results. Tell the user no matches were found.",
            };
        return new() { Detected = false };
    }

    private static HallucinationResult DetectRunsHallucination(
        List<ToolCallInfo> toolCalls,
        List<string> toolResults,
        string content
    )
    {
        var runCalls = toolCalls.Where(tc => tc.ToolName == AgentToolNames.ListJobRuns).ToList();
        if (runCalls.Count == 0)
            return new() { Detected = false };
        var allResultText = string.Join("\n", runCalls.Select(tc => tc.Result ?? ""));
        if (allResultText.Contains("Job runs: 0") && content.Length > 50)
        {
            var statusWords = new[]
            {
                "completed",
                "failed",
                "running",
                "pending",
                "success",
                "error",
            };
            if (statusWords.Any(s => content.ToLowerInvariant().Contains(s)))
                return new()
                {
                    Detected = true,
                    Reason = "Agent described job runs when list_job_runs returned 0 runs",
                    CorrectionPrompt =
                        "There are no job runs to describe. The tool returned 0 runs.",
                };
        }
        return new() { Detected = false };
    }

    private static HallucinationResult DetectJobsHallucination(
        List<ToolCallInfo> toolCalls,
        List<string> toolResults,
        string content
    )
    {
        var jobCalls = toolCalls.Where(tc => tc.ToolName == AgentToolNames.ListJobs).ToList();
        if (jobCalls.Count == 0)
            return new() { Detected = false };
        var allResultText = string.Join("\n", jobCalls.Select(tc => tc.Result ?? ""));
        if (allResultText.Contains("Jobs: 0") && content.Length > 50)
            return new()
            {
                Detected = true,
                Reason = "Agent described jobs when list_jobs returned 0 jobs",
                CorrectionPrompt = "There are no jobs in this project. The tool returned 0 jobs.",
            };
        return new() { Detected = false };
    }

    private static HallucinationResult DetectErrorMaskingHallucination(
        List<ToolCallInfo> toolCalls,
        string content
    )
    {
        var failedCalls = toolCalls
            .Where(tc =>
                (
                    tc.Status == AgentToolCallStatus.Error
                    || (tc.Result != null && tc.Result.StartsWith("Error:"))
                )
                && tc.ToolName != AgentToolNames.ShowArtifact
            )
            .ToList();
        if (failedCalls.Count == 0 || content.Length < 60)
            return new() { Detected = false };
        var acknowledgesError =
            content.Contains("error", StringComparison.OrdinalIgnoreCase)
            || content.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || content.Contains("couldn't", StringComparison.OrdinalIgnoreCase)
            || content.Contains("unable", StringComparison.OrdinalIgnoreCase)
            || content.Contains("sorry", StringComparison.OrdinalIgnoreCase)
            || content.Contains("no worries", StringComparison.OrdinalIgnoreCase);
        var failedCall = failedCalls[0];
        var failedResult = failedCall.Result ?? string.Empty;
        if (!acknowledgesError)
            return new()
            {
                Detected = true,
                Reason = $"Agent produced content after {failedCall.ToolName} error",
                CorrectionPrompt =
                    $"The tool {failedCall.ToolName} returned an error: {(failedResult.Length > 200 ? failedResult[..200] + "…" : failedResult)}. Acknowledge the error to the user.",
            };
        return new() { Detected = false };
    }

    private static HallucinationResult DetectIntentMismatch(string content)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Length < 20)
            return new() { Detected = false };
        var lower = content.ToLowerInvariant();
        var rephrasePatterns = new[]
        {
            "you asked about",
            "you want to know",
            "your question is",
            "you're asking",
            "the question asks",
            "you're looking for",
        };
        if (rephrasePatterns.Any(p => lower.Contains(p)) && content.Length < 150)
        {
            var answerIndicators = new[]
            {
                "here",
                "the answer",
                "is ",
                "are ",
                "was ",
                "has ",
                "shows",
                "includes",
                "contains",
            };
            if (!(answerIndicators.Any(a => lower.Contains(a)) && content.Length > 80))
                return new()
                {
                    Detected = true,
                    Reason = "Response rephrases the question without answering it",
                    CorrectionPrompt =
                        "You rephrased the user's question but didn't answer it. Use the available tools to find the answer and provide it directly.",
                };
        }
        var fillerWords = new[]
        {
            "basically",
            "essentially",
            "actually",
            "literally",
            "just",
            "well",
            "so",
            "like",
            "um",
            "uh",
            "hmm",
        };
        var fillerCount = fillerWords.Count(f =>
            System.Text.RegularExpressions.Regex.IsMatch(
                lower,
                $@"\b{System.Text.RegularExpressions.Regex.Escape(f)}\b"
            )
        );
        if (fillerCount >= 4 && content.Length < 200)
            return new()
            {
                Detected = true,
                Reason = $"Excessive filler words ({fillerCount})",
                CorrectionPrompt =
                    "Your response contained too many filler words. Please give a direct, clear answer without padding.",
            };
        return new() { Detected = false };
    }

    private static HashSet<string> ExtractMeaningfulWords(string text)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the",
            "and",
            "for",
            "are",
            "but",
            "not",
            "you",
            "all",
            "can",
            "had",
            "her",
            "was",
            "one",
            "our",
            "out",
            "has",
            "his",
            "how",
            "its",
            "may",
            "new",
            "now",
            "old",
            "see",
            "way",
            "who",
            "did",
            "get",
            "let",
            "say",
            "she",
            "too",
            "use",
            "this",
            "that",
            "with",
            "have",
            "from",
            "they",
            "been",
            "said",
            "each",
            "make",
            "like",
            "just",
            "over",
            "such",
            "take",
            "year",
            "them",
            "some",
            "than",
            "time",
            "very",
            "when",
            "come",
            "could",
            "what",
            "there",
            "result",
            "results",
            "rows",
            "row",
            "table",
            "data",
            "matches",
            "match",
            "score",
        };
        return text.Split(
                new[]
                {
                    ' ',
                    '\t',
                    '\n',
                    '\r',
                    ',',
                    '.',
                    ';',
                    ':',
                    '!',
                    '?',
                    '(',
                    ')',
                    '"',
                    '\'',
                    '/',
                    '|',
                    '-',
                },
                StringSplitOptions.RemoveEmptyEntries
            )
            .Select(w => w.ToLowerInvariant())
            .Where(w => w.Length >= 3 && !stopWords.Contains(w))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    // ── Friendly quips ───────────────────────────────────────────────────────

    private static readonly string[] FriendlyLoadingQuips = new[]
    {
        "Cool sussing that out for you…",
        "Give us a sec working out what you need…",
        "Righto, figuring out the best way to answer this…",
        "Hang tight cobber, I'm on it…",
        "Sweet as, just pulling that together…",
        "One sec legend, sorting it out…",
    };

    private static string FriendlyLoadingQuip() =>
        FriendlyLoadingQuips[Random.Shared.Next(FriendlyLoadingQuips.Length)];

    // ── Utility methods ──────────────────────────────────────────────────────

    private static bool IsTransientError(string error)
    {
        if (string.IsNullOrEmpty(error))
            return false;
        var lower = error.ToLowerInvariant();
        return lower.Contains("timeout")
            || lower.Contains("econnreset")
            || lower.Contains("econnrefused")
            || lower.Contains("socket")
            || lower.Contains("503")
            || lower.Contains("502")
            || lower.Contains("429")
            || lower.Contains("rate limit");
    }

    public static string SanitizeFileName(string name)
    {
        var cleaned = string.Concat(
            name.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-')
        );
        return cleaned.Length > 80 ? cleaned[..80] : cleaned;
    }

    public static string ContentTypeFor(string fileName) =>
        System.IO.Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".csv" => "text/csv",
            ".tsv" => "text/tab-separated-values",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".yaml" or ".yml" => "application/yaml",
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".txt" or ".md" or ".log" or ".sql" => "text/plain",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream",
        };

    public string FormatToolResult(string result) => FormatToolResultPresentation(result);

    public static string FormatToolResultPresentation(string result)
    {
        if (string.IsNullOrWhiteSpace(result))
            return "<em>Empty result</em>";
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var sb = new System.Text.StringBuilder();
        var separatorIdx = Array.FindIndex(lines, l => l.Trim() == "---");
        if (separatorIdx > 0)
        {
            var headerLine = lines[separatorIdx - 1];
            var headers = headerLine.Split(
                '|',
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
            );
            var dataLines = lines.Skip(separatorIdx + 1).ToArray();
            sb.Append("<table class=\"tool-table\"><thead><tr>");
            foreach (var h in headers)
                sb.Append($"<th>{System.Net.WebUtility.HtmlEncode(h)}</th>");
            sb.Append("</tr></thead><tbody>");
            foreach (var row in dataLines)
            {
                var cells = row.Split(
                    '|',
                    StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
                );
                sb.Append("<tr>");
                foreach (var c in cells)
                    sb.Append($"<td>{System.Net.WebUtility.HtmlEncode(c)}</td>");
                sb.Append("</tr>");
            }
            sb.Append("</tbody></table>");
            return sb.ToString();
        }
        var listItems = lines.Where(l => l.TrimStart().StartsWith("- ")).ToArray();
        if (listItems.Length > 0)
        {
            var headerLine = lines.FirstOrDefault(l => !l.TrimStart().StartsWith("- "));
            if (!string.IsNullOrEmpty(headerLine))
                sb.Append(
                    $"<div class=\"tool-result-header\">{System.Net.WebUtility.HtmlEncode(headerLine)}</div>"
                );
            sb.Append("<ul class=\"tool-list\">");
            foreach (var item in listItems)
            {
                var text = item.TrimStart()[2..];
                var formatted = System.Text.RegularExpressions.Regex.Replace(
                    System.Net.WebUtility.HtmlEncode(text),
                    @"\(([^)]+)\)",
                    "<span class=\"tool-meta\">($1)</span>"
                );
                sb.Append($"<li>{formatted}</li>");
            }
            sb.Append("</ul>");
            return sb.ToString();
        }
        var escaped = System.Net.WebUtility.HtmlEncode(result).Replace("\n", "<br/>");
        return $"<div class=\"tool-plain\">{escaped}</div>";
    }
}
