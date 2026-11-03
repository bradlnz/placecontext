using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text;
using PlaceContext.Application.Build;
using PlaceContext.Application.Shared;
using PlaceContext.Infrastructure.Workload;

namespace PlaceContext.Infrastructure.Build;

public sealed class LocalCliModels : IAsyncDisposable
{
    private CliAgentSession? _codex;
    private string? _codexModel;
    public static bool Supports(string protocol) => protocol is "codex" or "claude-code";
    public static string Endpoint(string protocol) => protocol switch
    {
        "codex" => "cli://codex",
        "claude-code" => "cli://claude",
        _ => throw new ArgumentException("Unsupported local CLI.")
    };
    private static string Executable(string protocol) => protocol == "codex" ? "codex" : "claude";

    public async Task<string[]?> DiscoverAsync(string protocol, CancellationToken ct)
    {
        if (!Supports(protocol))
        {
            throw new ArgumentException("Unsupported local CLI.");
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            // Check the actual executable before invoking it; absence is different from a signed-out CLI.
            if (!(Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                .Any(dir => File.Exists(Path.Combine(dir, Executable(protocol) + (OperatingSystem.IsWindows() ? ".exe" : "")))))
            {
                return null;
            }
            if (protocol == "codex")
            {
                await using var session = new CliAgentSession("");
                return await session.ListModelsAsync(timeout.Token);
            }
            var start = StartInfo("claude", Path.GetTempPath());
            foreach (var arg in new[] { "auth", "status", "--json" })
            {
                start.ArgumentList.Add(arg);
            }
            var result = await RunAsync(start, "", timeout.Token);
            return JsonNode.Parse(result.Output)?["loggedIn"]?.GetValue<bool>() == true ? ["default", "sonnet", "opus", "haiku"] : [];
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return [];
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or IOException)
        {
            return [];
        }
    }

    public async Task<JsonObject> CompleteAsync(string protocol, string model, JsonArray messages, JsonArray tools, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(BuildExecutionLimits.ModelResponse);
        try
        {
            if (protocol == "codex")
            {
                var first = _codex is null || _codexModel != model;
                if (first)
                {
                    if (_codex is not null)
                    {
                        await _codex.DisposeAsync();
                    }
                    _codex = new CliAgentSession(model);
                    _codexModel = model;
                }
                return await _codex!.NextAsync(first ? Transcript(messages) : messages, tools, timeout.Token);
            }
            if (protocol != "claude-code")
            {
                throw new ArgumentException("Unsupported local CLI.");
            }
            var input = Transcript(messages);
            var directory = Path.Combine(Path.GetTempPath(), "pc-claude-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            try
            {
                var start = ClaudeStartInfo(directory, model);
                var userContent = new MessagesProtocol().Request(model, input, [])["messages"]![0]!["content"]!.AsArray();
                userContent.Insert(0, new JsonObject
                {
                    ["type"] = "text",
                    ["text"] =
                    $"System instructions:\n{input[0]!["content"]!.GetValue<string>()}\nAvailable application tools:\n{tools.ToJsonString()}"
                });
                var request = new JsonObject { ["type"] = "user", ["message"] = new JsonObject { ["role"] = "user", ["content"] = userContent.DeepClone() } };
                var result = await RunAsync(start, request.ToJsonString() + "\n", timeout.Token);
                if (result.ExitCode != 0)
                {
                    throw new InvalidOperationException("Claude Code failed. Check claude auth status and model access on the app host.");
                }
                return ClaudeResponse(JsonNode.Parse(result.Output)?.AsObject() ?? throw new InvalidOperationException("Claude Code returned no response."), tools);
            }
            catch (Win32Exception)
            {
                throw new InvalidOperationException("Install Claude Code and run claude auth login on the app host.");
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (OperationCanceledException ex) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TimeoutException($"The {protocol} model response exceeded its {BuildExecutionLimits.ModelResponse.TotalMinutes:0}-minute limit. No further actions were started.", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_codex is not null)
        {
            await _codex.DisposeAsync();
        }
        _codex = null;
    }

    internal static JsonArray Transcript(JsonArray messages)
    {
        var history = new JsonArray(messages.Skip(1).Select(x => x!.DeepClone()).ToArray());
        var content = new JsonArray();
        foreach (var message in history)
        {
            if (message?["content"] is JsonArray parts)
            {
                for (var i = 0; i < parts.Count; i++)
                {
                    if (parts[i]?["type"]?.GetValue<string>() == "image_url")
                    {
                        content.Add(parts[i]!.DeepClone());
                        parts[i] = new JsonObject { ["type"] = "text", ["text"] = $"[Attached image {content.Count}]" };
                    }
                }
            }
        }
        content.Insert(0, new JsonObject
        {
            ["type"] = "text",
            ["text"] =
                            PromptText.Read("Build.Prompts.ContinueConversation") + history.ToJsonString()
        });
        return new JsonArray(messages[0]!.DeepClone(), new JsonObject { ["role"] = "user", ["content"] = content });
    }

    internal static ProcessStartInfo ClaudeStartInfo(string directory, string model)
    {
        var start = StartInfo("claude", directory);
        foreach (var arg in new[] { "--print", "--safe-mode", "--tools", "", "--strict-mcp-config", "--mcp-config", "{\"mcpServers\":{}}",
            "--disable-slash-commands", "--no-chrome", "--no-session-persistence", "--permission-mode", "dontAsk",
            "--settings", "{\"disableAllHooks\":true}", "--input-format", "stream-json", "--output-format", "json",
            "--system-prompt", PromptText.Read("Build.Prompts.ModelAdapter"),
            "--json-schema", """
            {"type":"object","additionalProperties":false,"properties":{"content":{"type":"string"},"tool_calls":{"type":"array","maxItems":8,"items":{"type":"object","additionalProperties":false,"properties":{"name":{"type":"string"},"arguments":{"type":"string"}},"required":["name","arguments"]}}},"required":["content","tool_calls"]}
            """ })
        {
            start.ArgumentList.Add(arg);
        }
        if (model != "default")
        {
            start.ArgumentList.Add("--model");
            start.ArgumentList.Add(model);
        }
        return start;
    }

    internal static JsonObject ClaudeResponse(JsonObject result, JsonArray tools)
    {
        if (result["is_error"]?.GetValue<bool>() == true || result["structured_output"] is not JsonObject reply)
        {
            throw new InvalidOperationException("Claude Code did not return a complete response. Check its login, model access, and usage limits.");
        }
        var calls = new JsonArray();
        var allowed = tools.Select(x => x!["function"]!["name"]!.GetValue<string>()).ToHashSet();
        if (reply["tool_calls"] is not JsonArray requested || requested.Count > 8)
        {
            throw new InvalidOperationException("Claude Code returned invalid tool calls.");
        }
        foreach (var call in requested)
        {
            var name = call?["name"]?.GetValue<string>() ?? "";
            var arguments = call?["arguments"]?.GetValue<string>() ?? "";
            if (!allowed.Contains(name) || JsonNode.Parse(arguments) is not JsonObject)
            {
                throw new InvalidOperationException("Claude Code returned an unknown tool or invalid arguments.");
            }
            calls.Add(new JsonObject
            {
                ["id"] = Guid.NewGuid().ToString("N"),
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = name, ["arguments"] = arguments }
            });
        }
        return new JsonObject { ["role"] = "assistant", ["content"] = reply["content"]?.GetValue<string>() ?? "", ["tool_calls"] = calls };
    }

    private static ProcessStartInfo StartInfo(string executable, string directory) => new(executable)
    {
        WorkingDirectory = directory,
        UseShellExecute = false,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };

    internal static async Task<(int ExitCode, string Output)> RunAsync(ProcessStartInfo start, string input, CancellationToken ct)
    {
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start local CLI.");
        using var cancel = ct.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException) { }
        });
        var output = ReadAsync(process.StandardOutput, retain: true, ct);
        var errors = ReadAsync(process.StandardError, retain: false, ct);
        try
        {
            await process.StandardInput.WriteAsync(input.AsMemory(), ct);
            process.StandardInput.Close();
            var response = await output;
            await errors;
            await process.WaitForExitAsync(ct);
            ct.ThrowIfCancellationRequested();
            return (process.ExitCode, response);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            await process.WaitForExitAsync();
            try
            {
                await Task.WhenAll(output, errors);
            }
            catch { /* Preserve the original process/read failure. */ }
        }
    }

    private static async Task<string> ReadAsync(StreamReader reader, bool retain, CancellationToken ct)
    {
        var text = new StringBuilder();
        var buffer = new char[8192];
        int read;
        while ((read = await reader.ReadAsync(buffer, ct)) > 0)
        {
            if (retain)
            {
                text.Append(buffer, 0, read);
                if (text.Length > 2_000_000)
                {
                    throw new InvalidOperationException("Local CLI returned an oversized response.");
                }
            }
        }
        return text.ToString();
    }
}
