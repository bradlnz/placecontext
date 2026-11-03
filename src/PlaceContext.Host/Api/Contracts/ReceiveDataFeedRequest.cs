using System.Text.Json;

namespace PlaceContext.Host.Api;

public sealed record ReceiveDataFeedRequest(JsonElement[] Records, string[]? DeletedKeys = null);
