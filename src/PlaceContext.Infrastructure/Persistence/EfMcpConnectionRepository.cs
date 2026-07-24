using Microsoft.EntityFrameworkCore;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfMcpConnectionRepository : IMcpConnectionRepository
{
    private readonly AppDbContext _db;

    public EfMcpConnectionRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<McpConnection>> ListByProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        var rows = await _db.McpConnections
            .Where(r => r.ProjectId == projectId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<McpConnection?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.McpConnections.FindAsync(new object[] { id }, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task AddAsync(McpConnection connection, CancellationToken ct = default)
    {
        _db.McpConnections.Add(ToRow(connection));
        await Task.CompletedTask;
    }

    public async Task UpdateAsync(McpConnection connection, CancellationToken ct = default)
    {
        var row = await _db.McpConnections.FindAsync(new object[] { connection.Id }, ct);
        if (row is not null)
        {
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
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.McpConnections.FindAsync(new object[] { id }, ct);
        if (row is not null) _db.McpConnections.Remove(row);
    }

    private static McpConnectionRow ToRow(McpConnection c) => new()
    {
        Id = c.Id,
        ProjectId = c.ProjectId,
        Name = c.Name,
        Transport = c.Transport,
        EndpointUrl = c.EndpointUrl,
        Command = c.Command,
        Args = c.Args,
        AuthType = c.AuthType,
        AuthToken = c.AuthToken,
        AuthHeader = c.AuthHeader,
        Enabled = c.Enabled,
        LastStatus = c.LastStatus,
        LastConnectedAt = c.LastConnectedAt,
        CreatedAt = c.CreatedAt,
    };

    private static McpConnection ToDomain(McpConnectionRow r) =>
        McpConnection.Rehydrate(r.Id, r.ProjectId, r.Name, r.Transport, r.EndpointUrl,
            r.Command, r.Args, r.AuthType, r.AuthToken, r.AuthHeader,
            r.Enabled, r.LastStatus, r.LastConnectedAt, r.CreatedAt);
}
