using System.Text.Json.Nodes;

namespace PlaceContext.Application.Build;

public interface IBuildModelGateway
{
    Task<JsonObject> CompleteAsync(Guid connectionId, string model, JsonArray messages, JsonArray tools, CancellationToken ct);
}
