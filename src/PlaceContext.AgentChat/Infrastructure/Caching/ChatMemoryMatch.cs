using System.Net.Http.Json;
using PlaceContext.Application.Ports;
using System.Text.Json;

namespace PlaceContext.AgentChat.Infrastructure.Caching;

/// <summary>A semantic search match from Qdrant.</summary>
public sealed record ChatMemoryMatch(string Content, string Role, Guid SessionId, float Score);
