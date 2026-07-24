using PlaceContext.Domain.Common;

namespace PlaceContext.Domain.Entities;

/// <summary>
/// Aggregate Root: an external MCP server connection. Each project can connect to multiple
/// MCP servers (HTTP, SSE, or stdio) to extend the agent's tool capabilities.
/// </summary>
public sealed class McpConnection : AggregateRoot
{
    private McpConnection(Guid id, Guid projectId, string name, string transport,
        string? endpointUrl, string? command, string? args, string? authType,
        string? authToken, string? authHeader, bool enabled, DateTimeOffset createdAt)
    {
        Id = id;
        ProjectId = projectId;
        Name = name;
        Transport = transport;
        EndpointUrl = endpointUrl;
        Command = command;
        Args = args;
        AuthType = authType;
        AuthToken = authToken;
        AuthHeader = authHeader;
        Enabled = enabled;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }
    public Guid ProjectId { get; }
    public string Name { get; private set; }
    public string Transport { get; private set; }  // "http" | "sse" | "stdio"
    public string? EndpointUrl { get; private set; }
    public string? Command { get; private set; }
    public string? Args { get; private set; }
    public string? AuthType { get; private set; }   // "none" | "bearer" | "header" | "apikey"
    public string? AuthToken { get; private set; }
    public string? AuthHeader { get; private set; }
    public bool Enabled { get; private set; }
    public string? LastStatus { get; private set; }
    public DateTimeOffset? LastConnectedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; }

    public static McpConnection Create(Guid projectId, string name, string transport,
        string? endpointUrl, string? command, string? args, string? authType,
        string? authToken, string? authHeader, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name required.", nameof(name));
        if (transport is not ("http" or "sse" or "stdio")) throw new ArgumentException("Invalid transport.", nameof(transport));
        return new McpConnection(Guid.NewGuid(), projectId, name.Trim(), transport,
            endpointUrl?.Trim(), command?.Trim(), args?.Trim(),
            authType ?? "none", authToken?.Trim(), authHeader?.Trim(), true, now);
    }

    public void Update(string name, string transport, string? endpointUrl, string? command, string? args,
        string? authType, string? authToken, string? authHeader, DateTimeOffset now)
    {
        Name = name.Trim();
        Transport = transport;
        EndpointUrl = endpointUrl?.Trim();
        Command = command?.Trim();
        Args = args?.Trim();
        AuthType = authType ?? "none";
        AuthToken = authToken?.Trim();
        AuthHeader = authHeader?.Trim();
    }

    public void SetEnabled(bool enabled, DateTimeOffset now)
    {
        Enabled = enabled;
    }

    public void RecordConnection(string status, DateTimeOffset now)
    {
        LastStatus = status;
        LastConnectedAt = now;
    }

    public static McpConnection Rehydrate(Guid id, Guid projectId, string name, string transport,
        string? endpointUrl, string? command, string? args, string? authType,
        string? authToken, string? authHeader, bool enabled, string? lastStatus,
        DateTimeOffset? lastConnectedAt, DateTimeOffset createdAt)
        => new(id, projectId, name, transport, endpointUrl, command, args,
            authType, authToken, authHeader, enabled, createdAt)
        {
            LastStatus = lastStatus,
            LastConnectedAt = lastConnectedAt,
        };
}
