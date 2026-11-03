using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace PlaceContext.Infrastructure.Build;

public sealed class ChatCompletionsProtocol : IModelProtocol
{
    public string CompletionPath => "chat/completions";

    public void Authenticate(HttpRequestMessage request, string key)
    {
        if (key.Length > 0)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }
    }

    public JsonObject Request(string model, JsonArray messages, JsonArray tools)
    {
        var request = new JsonObject { ["model"] = model, ["messages"] = messages.DeepClone() };
        if (tools.Count > 0)
        {
            request["tools"] = tools.DeepClone();
            request["parallel_tool_calls"] = false;
        }
        return request;
    }

    public JsonObject Response(JsonObject body)
    {
        var choice = body["choices"]?[0];
        if (choice?["finish_reason"]?.GetValue<string>() is "length" or "content_filter")
        {
            throw new InvalidOperationException("The model response was incomplete. No actions from this response were executed.");
        }
        return choice?["message"]?.DeepClone().AsObject() ?? throw new InvalidOperationException("The model returned no message.");
    }

    public (long? InputTokens, long? OutputTokens) Usage(JsonObject body)
    {
        if (body["usage"] is not JsonObject usage
            || usage["prompt_tokens"] is not JsonValue input || !input.TryGetValue<long>(out var inputTokens) || inputTokens < 0
            || usage["completion_tokens"] is not JsonValue output || !output.TryGetValue<long>(out var outputTokens) || outputTokens < 0)
        {
            return (null, null);
        }
        return (inputTokens, outputTokens);
    }
}
