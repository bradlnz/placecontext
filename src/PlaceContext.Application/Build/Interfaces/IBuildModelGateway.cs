using System.Text.Json.Nodes;

namespace PlaceContext.Application.Build;

public interface IBuildModelGateway
{
    ValueTask ResetSessionAsync() => ValueTask.CompletedTask;
    Task<JsonObject> CompleteAsync(Guid connectionId, string model, JsonArray messages, JsonArray tools, CancellationToken ct, Action<string>? report = null);
}
