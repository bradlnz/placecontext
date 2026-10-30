using System.Text.Json.Nodes;

namespace PlaceContext.Infrastructure.Build;

public interface IModelProtocol
{
    string CompletionPath { get; }
    void Authenticate(HttpRequestMessage request, string key);
    JsonObject Request(string model, JsonArray messages, JsonArray tools);
    JsonObject Response(JsonObject body);
    (long? InputTokens, long? OutputTokens) Usage(JsonObject body);
}
