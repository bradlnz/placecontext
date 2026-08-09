using Microsoft.EntityFrameworkCore;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Mcp.Infrastructure.Persistence;

public sealed class EfMcpConnectionRepository(McpDbContext db) : IMcpConnectionRepository
{
    public async Task<IReadOnlyList<McpConnection>> ListByProjectAsync(
        Guid projectId,
        CancellationToken ct = default)
        => (await db.McpConnections
                .Where(row => row.ProjectId == projectId)
                .OrderByDescending(row => row.CreatedAt)
                .ToListAsync(ct))
            .Select(ToDomain)
            .ToList();

    public async Task<McpConnection?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var row = await db.McpConnections.SingleOrDefaultAsync(value => value.Id == id, ct);
        return row is null ? null : ToDomain(row);
    }

    public Task AddAsync(McpConnection connection, CancellationToken ct = default)
        => db.McpConnections.AddAsync(ToRow(connection), ct).AsTask();

    public async Task UpdateAsync(McpConnection connection, CancellationToken ct = default)
    {
        var row = await db.McpConnections.SingleOrDefaultAsync(value => value.Id == connection.Id, ct);
        if (row is null)
            return;

        row.Name = connection.Name;
        row.Transport = connection.Transport;
        row.EndpointUrl = connection.EndpointUrl;
        row.Command = connection.Command;
        row.Args = connection.Args;
        row.AuthType = connection.AuthType;
        row.AuthToken = connection.AuthToken;
        row.AuthHeader = connection.AuthHeader;
        row.Enabled = connection.Enabled;
        row.LastStatus = connection.LastStatus;
        row.LastConnectedAt = connection.LastConnectedAt;
        row.OAuthAccessToken = connection.OAuthAccessToken;
        row.OAuthRefreshToken = connection.OAuthRefreshToken;
        row.OAuthTokenExpiresAt = connection.OAuthTokenExpiresAt;
        row.OAuthClientId = connection.OAuthClientId;
        row.OAuthScopes = connection.OAuthScopes;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var row = await db.McpConnections.SingleOrDefaultAsync(value => value.Id == id, ct);
        if (row is not null)
            db.McpConnections.Remove(row);
    }

    private static McpConnectionRow ToRow(McpConnection connection)
        => new()
        {
            Id = connection.Id,
            ProjectId = connection.ProjectId,
            Name = connection.Name,
            Transport = connection.Transport,
            EndpointUrl = connection.EndpointUrl,
            Command = connection.Command,
            Args = connection.Args,
            AuthType = connection.AuthType,
            AuthToken = connection.AuthToken,
            AuthHeader = connection.AuthHeader,
            Enabled = connection.Enabled,
            LastStatus = connection.LastStatus,
            LastConnectedAt = connection.LastConnectedAt,
            CreatedAt = connection.CreatedAt,
            OAuthAccessToken = connection.OAuthAccessToken,
            OAuthRefreshToken = connection.OAuthRefreshToken,
            OAuthTokenExpiresAt = connection.OAuthTokenExpiresAt,
            OAuthClientId = connection.OAuthClientId,
            OAuthScopes = connection.OAuthScopes,
        };

    private static McpConnection ToDomain(McpConnectionRow row)
        => McpConnection.Rehydrate(
            row.Id,
            row.ProjectId,
            row.Name,
            row.Transport,
            row.EndpointUrl,
            row.Command,
            row.Args,
            row.AuthType,
            row.AuthToken,
            row.AuthHeader,
            row.Enabled,
            row.LastStatus,
            row.LastConnectedAt,
            row.CreatedAt,
            row.OAuthAccessToken,
            row.OAuthRefreshToken,
            row.OAuthTokenExpiresAt,
            row.OAuthClientId,
            row.OAuthScopes);
}
