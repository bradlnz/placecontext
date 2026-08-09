using System.Text.Json.Serialization;
namespace PlaceContext.AgentChat.Contracts.Api;

public sealed record AgentStreamRequest(
    string Message,
    string? Context,
    [property: JsonPropertyName("correlation_id")] string? CorrelationId);
