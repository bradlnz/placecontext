using System.Text.Json.Nodes;

namespace PlaceContext.Infrastructure.Build;

public sealed class MessagesProtocol : IModelProtocol
{
    public string CompletionPath => "messages";
    public void Authenticate(HttpRequestMessage request, string key)
    {
        request.Headers.Add("anthropic-version", "2023-06-01");
        if (key.Length > 0)
        {
            request.Headers.Add("x-api-key", key);
        }
    }

    public (long? InputTokens, long? OutputTokens) Usage(JsonObject body)
    {
        if (body["usage"] is not JsonObject usage)
        {
            return (null, null);
        }
        long? Read(string name) => usage[name] is JsonValue value && value.TryGetValue<long>(out var count) && count >= 0 ? count : null;
        var input = Read("input_tokens");
        var output = Read("output_tokens");
        if (input is null || output is null)
        {
            return (null, null);
        }
        foreach (var key in new[] { "cache_creation_input_tokens", "cache_read_input_tokens" })
        {
            if (!usage.ContainsKey(key))
            {
                continue;
            }
            var cached = Read(key);
            if (cached is null || input > long.MaxValue - cached)
            {
                return (null, null);
            }
            input += cached;
        }
        return (input, output);
    }
    public JsonObject Request(string model, JsonArray messages, JsonArray tools)
    {
        var converted = new JsonArray();
        foreach (var message in messages.Where(x => x?["role"]?.GetValue<string>() != "system"))
        {
            var role = message!["role"]!.GetValue<string>();
            var content = new JsonArray();
            void AppendContent()
            {
                if (role == "tool")
                {
                    content.Add(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = message["tool_call_id"]!.DeepClone(), ["content"] = message["content"]!.DeepClone() });
                    return;
                }
                void AppendMessageContent()
                {
                    if (message["content"] is JsonArray parts)
                    {
                        foreach (var part in parts)
                        {
                            if (part?["type"]?.GetValue<string>() == "image_url")
                            {
                                var url = part["image_url"]!["url"]!.GetValue<string>();
                                var split = url.IndexOf(',');
                                if (!url.StartsWith("data:image/", StringComparison.Ordinal) || split < 0)
                                {
                                    throw new ArgumentException("Only uploaded images are supported.");
                                }
                                content.Add(new JsonObject
                                {
                                    ["type"] = "image",
                                    ["source"] = new JsonObject
                                    {
                                        ["type"] = "base64",
                                        ["media_type"] = url[5..url.IndexOf(';')],
                                        ["data"] = url[(split + 1)..]
                                    }
                                });
                                continue;
                            }
                            content.Add(part!.DeepClone());
                        }
                        return;
                    }
                    if (message["content"]?.GetValue<string>() is { Length: > 0 } text)
                    {
                        content.Add(new JsonObject { ["type"] = "text", ["text"] = text });
                    }
                }
                AppendMessageContent();
                if (message["tool_calls"] is JsonArray calls)
                {
                    foreach (var call in calls)
                    {
                        content.Add(new JsonObject
                        {
                            ["type"] = "tool_use",
                            ["id"] = call!["id"]!.DeepClone(),
                            ["name"] = call["function"]!["name"]!.DeepClone(),
                            ["input"] = JsonNode.Parse(call["function"]!["arguments"]!.GetValue<string>())
                        });
                    }
                }
            }
            AppendContent();
            var targetRole = role == "tool" ? "user" : role;
            if (converted.LastOrDefault() is JsonObject last && last["role"]!.GetValue<string>() == targetRole)
            {
                foreach (var part in content)
                {
                    last["content"]!.AsArray().Add(part!.DeepClone());
                }
                continue;
            }
            converted.Add(new JsonObject { ["role"] = targetRole, ["content"] = content });
        }
        return new()
        {
            ["model"] = model,
            ["max_tokens"] = 8192,
            ["system"] = string.Join("\n", messages.Where(x => x?["role"]?.GetValue<string>() == "system").Select(x => x!["content"]!.GetValue<string>())),
            ["messages"] = converted,
            ["tools"] = new JsonArray(tools.Select(x => (JsonNode)new JsonObject
            {
                ["name"] = x!["function"]!["name"]!.DeepClone(),
                ["description"] = x["function"]!["description"]!.DeepClone(),
                ["input_schema"] = x["function"]!["parameters"]!.DeepClone()
            }).ToArray())
        };
    }
    public JsonObject Response(JsonObject body)
    {
        if (body["stop_reason"]?.GetValue<string>() == "max_tokens")
        {
            throw new InvalidOperationException("The model response was incomplete. No actions from this response were executed.");
        }
        var parts = body["content"]?.AsArray() ?? throw new InvalidOperationException("The model returned no message.");
        var calls = new JsonArray(parts.Where(x => x?["type"]?.GetValue<string>() == "tool_use")
            .Select(x => (JsonNode)new JsonObject
            {
                ["id"] = x!["id"]!.DeepClone(),
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = x["name"]!.DeepClone(), ["arguments"] = x["input"]!.ToJsonString() }
            }).ToArray());
        var result = new JsonObject { ["role"] = "assistant", ["content"] = string.Join("\n", parts.Where(x => x?["type"]?.GetValue<string>() == "text").Select(x => x!["text"]!.GetValue<string>())) };
        if (calls.Count > 0)
        {
            result["tool_calls"] = calls;
        }
        return result;
    }
}
