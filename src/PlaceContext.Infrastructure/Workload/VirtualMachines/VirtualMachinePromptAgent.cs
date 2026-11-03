using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Shared;
using PlaceContext.Application.Workload.VirtualMachines;
namespace PlaceContext.Infrastructure.Workload;

public sealed class VirtualMachinePromptAgent(IHttpClientFactory clients, IOptions<VirtualMachineEnvironmentOptions> options) : IVirtualMachineTaskAgent
{
    public string Model => options.Value.Agent.Model;
    public string? ConfigurationError =>
        options.Value.Agent.Provider is not ("chat-completions" or "codex") ? "QEMU agent Provider must be chat-completions or codex."
            : options.Value.Agent.Provider != "codex" && (!Uri.TryCreate(options.Value.Agent.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https") || endpoint.UserInfo.Length > 0)
            ? "Configure PlaceContext:Qemu:Agent:Endpoint with a chat-completions URL."
            : string.IsNullOrWhiteSpace(Model) ? "Configure PlaceContext:Qemu:Agent:Model with a vision model that supports tool calls."
            : options.Value.Agent.MaxSteps is < 1 or > 100 ? "QEMU agent MaxSteps must be between 1 and 100." : null;

    private static readonly string SystemPrompt = PromptText.Read("Workload.VirtualMachines.Prompts.DesktopTask");

    private static readonly JsonNode Tools = JsonNode.Parse("""
        [
          {"type":"function","function":{"name":"guest_shell","description":"Execute a bounded shell command as the non-root desktop user inside the disposable guest.","parameters":{"type":"object","properties":{"command":{"type":"string"},"description":{"type":"string","description":"Short user-facing action description, without secrets or reasoning."}},"required":["command","description"],"additionalProperties":false}}},
          {"type":"function","function":{"name":"live_desktop","description":"Read a frame from the live desktop preview. Returns an image in the following message.","parameters":{"type":"object","properties":{},"additionalProperties":false}}},
          {"type":"function","function":{"name":"finish","description":"Finish the task with its verified result or a clear failure reason.","parameters":{"type":"object","properties":{"success":{"type":"boolean"},"summary":{"type":"string"}},"required":["success","summary"],"additionalProperties":false}}}
        ]
        """)!;

    public async Task<VirtualMachineEnvironmentResult> RunAsync(string prompt, string? input,
        Func<string, int, CancellationToken, Task<VirtualMachineEnvironmentResult>> executeGuest, CancellationToken ct,
        Func<VirtualMachineReplayCapture, CancellationToken, Task>? recordFrame = null, VirtualMachineAgentInteraction? interaction = null, Func<CancellationToken, Task<byte[]>>? readLiveFrame = null)
    {
        if (ConfigurationError is { } error)
        {
            throw new InvalidOperationException(error);
        }
        if ((input?.Length ?? 0) > 65_536)
        {
            throw new InvalidOperationException("QEMU agent context must be no larger than 65,536 characters.");
        }
        var activities = new List<VirtualMachineAgentActivity>();
        var modelTurns = 0;
        var elapsed = Stopwatch.StartNew();
        var frameSequence = 0;
        async Task RecordAsync(string description, int? exitCode, string? screenshot = null)
        {
            if (recordFrame is null || frameSequence >= IVirtualMachineReplayStore.MaxFrames)
            {
                return;
            }
            byte[]? png = null;
            string? captureError = null;
            try
            {
                png = Convert.FromBase64String(screenshot ?? await ReadFrameAsync(readLiveFrame, ct));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or FormatException)
            {
                captureError = "Live preview frame unavailable for this action.";
            }
            await recordFrame(new(new(frameSequence++, elapsed.ElapsedMilliseconds, DateTimeOffset.UtcNow,
                description, exitCode, png is not null, captureError), png), ct);
        }
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = SystemPrompt },
            new JsonObject { ["role"] = "user", ["content"] = PromptText.Render("Workload.VirtualMachines.Prompts.TaskContext", ("task", prompt), ("context", input ?? "{}")) },
        };
        var ready = await executeGuest("command -v chromium >/dev/null && command -v xdotool >/dev/null", 8192, ct);
        if (ready.ExitCode != 0)
        {
            return Failed("The guest image must provide Chromium and xdotool.", activities, modelTurns);
        }
        try
        {
            var initialScreen = await AddFrameAsync(messages, readLiveFrame, ct);
            await RecordAsync("Desktop ready", null, initialScreen);
            await using var codex = options.Value.Agent.Provider == "codex" ? new CliAgentSession(Model) : null;
            using var client = clients.CreateClient("qemu-agent");
            for (var step = 0; step < options.Value.Agent.MaxSteps; step++)
            {
                ct.ThrowIfCancellationRequested();
                modelTurns = step + 1;
                var request = new HttpRequestMessage(HttpMethod.Post, options.Value.Agent.Endpoint)
                {
                    Content = JsonContent.Create(new { model = Model, messages, tools = Tools, tool_choice = "auto", parallel_tool_calls = false, max_tokens = 2048 }),
                };
                using (request)
                {
                    var (nextMessage, failure) = await NextMessageAsync();
                    if (failure is not null)
                    {
                        return failure;
                    }
                    var message = nextMessage!;

                    async Task<(JsonObject? Message, VirtualMachineEnvironmentResult? Failure)> NextMessageAsync()
                    {
                        if (codex is not null)
                        {
                            return (await codex.NextAsync(messages, Tools.AsArray(), ct), null);
                        }
                        if (!string.IsNullOrWhiteSpace(options.Value.Agent.ApiKey))
                        {
                            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.Agent.ApiKey);
                        }
                        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                        if (!response.IsSuccessStatusCode)
                        {
                            return (null, Failed($"The agent model returned HTTP {(int)response.StatusCode}. Check its endpoint, credentials, and vision/tool support.", activities, modelTurns));
                        }
                        await response.Content.LoadIntoBufferAsync(1_048_576, ct);
                        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
                        return (body?["choices"]?[0]?["message"]?.AsObject()
                            ?? throw new InvalidOperationException("The model returned no message."), null);
                    }
                    // Keep only protocol fields; provider-specific reasoning is not stored or displayed.
                    messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = message["content"]?.DeepClone(), ["tool_calls"] = message["tool_calls"]?.DeepClone() });
                    if (message["tool_calls"] is not JsonArray calls || calls.Count == 0)
                    {
                        messages.Add(new JsonObject { ["role"] = "user", ["content"] = PromptText.Read("Workload.VirtualMachines.Prompts.RequestTool") });
                        continue;
                    }
                    if (calls.Count != 1)
                    {
                        return Failed("The model must request one tool at a time. Parallel desktop actions are not supported.", activities, modelTurns);
                    }
                    var call = calls[0]!;
                    var id = call["id"]?.GetValue<string>() ?? throw new InvalidOperationException("The model returned a tool call without an ID.");
                    var name = call["function"]?["name"]?.GetValue<string>();
                    var args = JsonNode.Parse(call["function"]?["arguments"]?.GetValue<string>() ?? "{}")!;
                    switch (name)
                    {
                        case "finish":
                            var success = args["success"]!.GetValue<bool>();
                            var fullSummary = args["summary"]!.GetValue<string>();
                            var summary = Limit(fullSummary, 8192);
                            if (string.IsNullOrWhiteSpace(summary))
                            {
                                return Failed("The agent finished without a result.", activities, modelTurns);
                            }
                            if (success && activities.Count == 0)
                            {
                                return Failed("The agent claimed success without taking or verifying an action.", activities, modelTurns);
                            }
                            return new(success ? 0 : 1, summary, success ? "" : summary, OutputTruncated: fullSummary.Length > 8192, AgentSteps: step + 1, Activity: activities);
                        case "guest_shell":
                            var command = args["command"]!.GetValue<string>();
                            if (string.IsNullOrWhiteSpace(command) || command.Length > 8192 || command.Contains('\0'))
                            {
                                throw new InvalidOperationException("The agent returned an invalid guest command.");
                            }
                            var result = await executeGuest(command, 8192, ct);
                            activities.Add(new(name, Limit(args["description"]?.GetValue<string>() ?? "Guest command", 200), result.ExitCode));
                            messages.Add(ToolResult(id, JsonSerializer.Serialize(new
                            {
                                exitCode = result.ExitCode,
                                stdout = result.Stdout,
                                stderr = result.Stderr,
                                truncated = result.OutputTruncated
                            })));
                            await RecordAsync(activities[^1].Description, result.ExitCode);
                            break;
                        case "live_desktop":
                            messages.Add(ToolResult(id, "Live desktop frame follows."));
                            var screen = await AddFrameAsync(messages, readLiveFrame, ct);
                            activities.Add(new(name, "Inspected the guest desktop", 0));
                            await RecordAsync(activities[^1].Description, 0, screen);
                            break;
                        default:
                            messages.Add(ToolResult(id, "Unknown tool. Use guest_shell, live_desktop, or finish."));
                            break;
                    }
                }
            }
            return Failed($"The agent reached its {options.Value.Agent.MaxSteps}-step limit without completing the task.", activities, modelTurns);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failed("The agent model request timed out.", activities, modelTurns);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return Failed("Could not contact the configured agent model.", activities, modelTurns);
        }
        catch (IOException)
        {
            return Failed("The live desktop feed is unavailable. Check the VM display connection.", activities, modelTurns);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or NullReferenceException)
        {
            return Failed("Agent execution failed: " + Limit(ex.Message, 500), activities, modelTurns);
        }
    }

    private static JsonObject ToolResult(string id, string content) => new() { ["role"] = "tool", ["tool_call_id"] = id, ["content"] = content };
    private static string Limit(string text, int limit) => text[..Math.Min(text.Length, limit)];
    private static VirtualMachineEnvironmentResult Failed(string reason, IReadOnlyList<VirtualMachineAgentActivity> activity, int turns) => new(1, "", reason, AgentSteps: turns, Activity: activity);

    private static async Task<string> ReadFrameAsync(Func<CancellationToken, Task<byte[]>>? readLiveFrame, CancellationToken ct)
    {
        if (readLiveFrame is null)
        {
            throw new InvalidOperationException("The live desktop feed is required.");
        }
        var bytes = await readLiveFrame(ct);
        if (bytes.Length is < 8 or > 3_000_000 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            throw new InvalidOperationException("The live desktop returned an invalid frame.");
        }
        return Convert.ToBase64String(bytes);
    }

    private static async Task<string> AddFrameAsync(JsonArray messages,
        Func<CancellationToken, Task<byte[]>>? readLiveFrame, CancellationToken ct)
    {
        var screenshot = await ReadFrameAsync(readLiveFrame, ct);
        // Retain only the latest image to bound the model context and request size.
        foreach (var old in messages.OfType<JsonObject>().Where(message => message["content"] is JsonArray))
        {
            old["content"] = PromptText.Read("Workload.VirtualMachines.Prompts.EarlierFrame");
        }
        messages.Add(new JsonObject
        {
            ["role"] = "user",
            ["content"] = new JsonArray
        {
            new JsonObject { ["type"] = "text", ["text"] = PromptText.Read("Workload.VirtualMachines.Prompts.CurrentFrame") },
            new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = "data:image/png;base64," + screenshot } },
        }
        });
        return screenshot;
    }
}
