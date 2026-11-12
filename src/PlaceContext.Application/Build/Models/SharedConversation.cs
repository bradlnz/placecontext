using System.Text.Json.Nodes;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Build;

public sealed record SharedConversation(string Title, JsonArray Messages, ChainRunView? Run,
    IReadOnlyList<SharedConversationArtifact> Artifacts, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
public sealed record SharedConversationArtifact(string Title, string ContentType, byte[] Content);
