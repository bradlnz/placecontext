namespace PlaceContext.Host.Api;

/// <summary>Shared request type for creating or provisioning a tenant workspace.</summary>
public sealed record CoreCreateProjectRequest(string Path, string? Name);
