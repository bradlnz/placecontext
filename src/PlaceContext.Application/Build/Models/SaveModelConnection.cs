using System.Text.Json.Nodes;

namespace PlaceContext.Application.Build;

public sealed record SaveModelConnection(Guid Id, string Name, string Protocol, string BaseUrl,
    IReadOnlyList<string> Models, bool Enabled, string? ApiKey = null, bool ClearApiKey = false);
