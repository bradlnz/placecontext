using System.Text.Json.Serialization;
namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record AgentStreamRequest(
    string Message,
    string? Context,
    [property: JsonPropertyName("correlation_id")] string? CorrelationId);