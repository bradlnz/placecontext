using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PlaceContext.Infrastructure.Workload;

// Adapt local Codex dynamic tools to the caller's permission-checked tool loop.
internal sealed class CodexQemuSession(string model, ProcessStartInfo? testStart = null) : IAsyncDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pc-codex-" + Guid.NewGuid().ToString("N"));
    private Process? _process;
    private Task? _stderr;
    private string? _threadId;
    private JsonNode? _pendingToolId;
    private int _requestId;
    private readonly char[] _readBuffer = new char[8192];
    private int _readOffset, _readCount;

    internal static ProcessStartInfo BuildStartInfo(string directory)
    {
        var start = new ProcessStartInfo("codex")
        {
            WorkingDirectory = directory, UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
        };
        foreach (var arg in new[] { "app-server", "--listen", "stdio://", "-c", "web_search=\"disabled\"",
            "-c", "project_doc_max_bytes=0", "-c", "features.skip_host_skill_discovery=true" }) start.ArgumentList.Add(arg);
        foreach (var feature in new[] { "shell_tool", "shell_snapshot", "apps", "plugins", "hooks", "multi_agent",
            "browser_use", "computer_use", "view_image", "image_generation", "memories", "skill_search", "code_mode", "in_app_browser", "tool_suggest" })
        { start.ArgumentList.Add("--disable"); start.ArgumentList.Add(feature); }
        return start;
    }

    private async Task InitializeAsync(CancellationToken ct)
    {
        if (_process is null)
        {
            Directory.CreateDirectory(_directory);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            try { _process = Process.Start(testStart ?? BuildStartInfo(_directory)) ?? throw new InvalidOperationException("Could not start Codex."); }
            catch (System.ComponentModel.Win32Exception) { throw new InvalidOperationException("Install Codex CLI on the workflow host and sign in with codex login before using the codex provider."); }
            _process.StandardInput.AutoFlush = true;
            _stderr = DrainAsync(_process.StandardError);
            await RequestAsync("initialize", new { clientInfo = new { name = "placecontext_qemu", title = "PlaceContext QEMU", version = "0.1.0" }, capabilities = new { experimentalApi = true } }, ct);
            await SendAsync(new { method = "initialized" }, ct);
        }
    }

    public async Task<string[]> ListModelsAsync(CancellationToken ct)
    {
        await InitializeAsync(ct);
        var account = await RequestAsync("account/read", new { refreshToken = false }, ct);
        if (account["account"] is null && account["requiresOpenaiAuth"]?.GetValue<bool>() != false) return [];
        var models = new List<string>();
        string? cursor = null;
        do
        {
            var page = await RequestAsync("model/list", new { limit = 100, cursor, includeHidden = false }, ct);
            foreach (var item in (page["data"] as JsonArray ?? []).OrderByDescending(x => x?["isDefault"]?.GetValue<bool>() == true))
                if (item?["model"]?.GetValue<string>() is { Length: > 0 and <= 200 } id && !models.Contains(id)) models.Add(id);
            cursor = page["nextCursor"]?.GetValue<string>();
        } while (cursor is not null && models.Count < 500);
        return models.Take(500).ToArray();
    }

    public async Task<JsonObject> NextAsync(JsonArray messages, JsonArray tools, CancellationToken ct)
    {
        await InitializeAsync(ct);
        if (_threadId is null)
        {
            var config = await RequestAsync("config/read", new { includeLayers = false }, ct);
            var overrides = new Dictionary<string, object>();
            // Empty TOML tables merge with user config; disable every configured server explicitly.
            if (config["config"]?["mcp_servers"] is JsonObject servers)
                foreach (var server in servers) overrides[$"mcp_servers.{server.Key}.enabled"] = false;
            var thread = await RequestAsync("thread/start", new
            {
                model, cwd = _directory, ephemeral = true, sandbox = "read-only", approvalPolicy = "never",
                environments = Array.Empty<object>(), config = overrides,
                baseInstructions = messages[0]!["content"]!.GetValue<string>(),
                dynamicTools = tools.Select(tool => new { type = "function", name = tool!["function"]!["name"]!.GetValue<string>(),
                    description = tool["function"]!["description"]!.GetValue<string>(), inputSchema = tool["function"]!["parameters"] }),
            }, ct);
            _threadId = thread["thread"]!["id"]!.GetValue<string>();
        }

        if (_pendingToolId is not null)
        {
            var toolIndex = messages.Select((message, index) => (message, index)).Last(pair => pair.message?["role"]?.GetValue<string>() == "tool").index;
            var content = new JsonArray(new JsonObject { ["type"] = "inputText", ["text"] = messages[toolIndex]!["content"]!.GetValue<string>() });
            foreach (var message in messages.Skip(toolIndex + 1))
                if (message?["content"] is JsonArray parts)
                    foreach (var part in parts.Where(part => part?["type"]?.GetValue<string>() == "image_url"))
                        content.Add(new JsonObject { ["type"] = "inputImage", ["imageUrl"] = part!["image_url"]!["url"]!.GetValue<string>() });
            await SendAsync(new { id = _pendingToolId, result = new { contentItems = content, success = true } }, ct);
            _pendingToolId = null;
        }
        else
        {
            var input = new JsonArray();
            // A prose-only completed turn gets just the existing loop's reminder on the next turn.
            var relevant = messages.Any(message => message?["role"]?.GetValue<string>() == "assistant")
                ? messages.TakeLast(1) : messages.Skip(1);
            foreach (var message in relevant)
            {
                if (message?["content"] is JsonArray parts)
                    foreach (var part in parts)
                        input.Add(part?["type"]?.GetValue<string>() == "image_url"
                            ? new JsonObject { ["type"] = "image", ["url"] = part["image_url"]!["url"]!.GetValue<string>() }
                            : new JsonObject { ["type"] = "text", ["text"] = part!["text"]!.GetValue<string>() });
                else input.Add(new JsonObject { ["type"] = "text", ["text"] = message!["content"]!.GetValue<string>() });
            }
            await RequestAsync("turn/start", new { threadId = _threadId, input }, ct);
        }
        var answer = new StringBuilder();
        while (true)
        {
            var notification = await ReadAsync(ct);
            var method = notification["method"]?.GetValue<string>();
            if (method == "item/completed" && notification["params"]?["item"]?["type"]?.GetValue<string>() == "agentMessage")
            {
                answer.AppendLine(notification["params"]!["item"]!["text"]?.GetValue<string>());
                if (answer.Length > 2_000_000) throw new InvalidOperationException("Codex returned an oversized answer.");
            }
            if (method == "item/tool/call")
            {
                var call = notification["params"]!;
                if (call["threadId"]?.GetValue<string>() != _threadId) throw new InvalidOperationException("Codex returned a tool call for another thread.");
                _pendingToolId = notification["id"]!.DeepClone();
                return new JsonObject { ["role"] = "assistant", ["tool_calls"] = new JsonArray(new JsonObject
                {
                    ["id"] = call["callId"]!.GetValue<string>(), ["type"] = "function",
                    ["function"] = new JsonObject { ["name"] = call["tool"]!.GetValue<string>(), ["arguments"] = call["arguments"]!.ToJsonString() },
                }) };
            }
            if (notification.ContainsKey("id") && method is not null)
                throw new InvalidOperationException("Codex requested an unsupported host operation; the task was stopped.");
            if (method == "turn/completed")
            {
                if (notification["params"]?["turn"]?["status"]?.GetValue<string>() != "completed")
                    throw new InvalidOperationException("The Codex turn failed or was interrupted. Check codex login status and model access on the workflow host.");
                return new JsonObject { ["role"] = "assistant", ["content"] = answer.Length > 0 ? answer.ToString().Trim() : "The Codex turn ended without calling finish." };
            }
        }
    }

    private async Task<JsonNode> RequestAsync(string method, object parameters, CancellationToken ct)
    {
        var id = ++_requestId;
        await SendAsync(new { id, method, @params = parameters }, ct);
        while (true)
        {
            var response = await ReadAsync(ct);
            if (response.ContainsKey("method") && response.ContainsKey("id"))
                throw new InvalidOperationException("Codex requested an unsupported host operation during setup.");
            if (response["id"]?.ToJsonString() != id.ToString()) continue;
            if (response.ContainsKey("error")) throw new InvalidOperationException($"Codex {method} failed. Check CLI compatibility, login, and model configuration.");
            return response["result"] ?? throw new InvalidOperationException("Codex returned no result.");
        }
    }

    private Task SendAsync(object message, CancellationToken ct) => _process!.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), ct);
    private async Task<JsonObject> ReadAsync(CancellationToken ct)
    {
        // Bound local protocol messages too; screenshot events can be larger than text tool calls.
        var text = new StringBuilder();
        while (true)
        {
            if (_readOffset == _readCount)
            {
                _readCount = await _process!.StandardOutput.ReadAsync(_readBuffer.AsMemory(), ct);
                _readOffset = 0;
                if (_readCount == 0) throw new InvalidOperationException("Codex app-server exited before completing the task. Check codex login status on the workflow host.");
            }
            var end = Array.IndexOf(_readBuffer, '\n', _readOffset, _readCount - _readOffset);
            var length = (end < 0 ? _readCount : end) - _readOffset;
            text.Append(_readBuffer, _readOffset, length);
            _readOffset += length + (end < 0 ? 0 : 1);
            if (text.Length > 8_000_000) throw new InvalidOperationException("Codex returned an oversized protocol message.");
            if (end >= 0) return JsonNode.Parse(text.ToString())!.AsObject();
        }
    }
    private static async Task DrainAsync(StreamReader reader)
    { var buffer = new char[4096]; while (await reader.ReadAsync(buffer) != 0) { } }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_process is not null)
            {
                if (!_process.HasExited) _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
                if (_stderr is not null) await _stderr;
                _process.Dispose();
            }
        }
        finally { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
    }
}
