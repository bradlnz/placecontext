using System.Text.Json;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfAgentChatSessionRepository : IAgentChatSessionRepository
{
    private readonly AppDbContext _db;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public EfAgentChatSessionRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(AgentChatSession session, CancellationToken ct = default)
        => await _db.AgentChatSessions.AddAsync(ToRow(session), ct);

    public async Task UpdateAsync(AgentChatSession session, CancellationToken ct = default)
    {
        var existing = await _db.AgentChatSessions.FindAsync(new object[] { session.Id }, ct);
        if (existing is null) return;

        var updated = ToRow(session);
        existing.Title = updated.Title;
        existing.MessagesJson = updated.MessagesJson;
        existing.UpdatedAt = updated.UpdatedAt;
    }

    public async Task<AgentChatSession?> GetByIdAsync(Guid sessionId, CancellationToken ct = default)
    {
        var row = await _db.AgentChatSessions.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == sessionId, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<AgentChatSession>> ListForProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        var rows = await _db.AgentChatSessions.AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .OrderByDescending(r => r.UpdatedAt)
            .Select(r => new AgentChatSessionRow
            {
                Id = r.Id,
                ProjectId = r.ProjectId,
                UserId = r.UserId,
                Title = r.Title,
                MessagesJson = r.MessagesJson,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
            })
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<AgentChatSession>> ListForUserAsync(Guid projectId, Guid userId, CancellationToken ct = default)
    {
        var rows = await _db.AgentChatSessions.AsNoTracking()
            .Where(r => r.ProjectId == projectId && r.UserId == userId)
            .OrderByDescending(r => r.UpdatedAt)
            .Select(r => new AgentChatSessionRow
            {
                Id = r.Id,
                ProjectId = r.ProjectId,
                UserId = r.UserId,
                Title = r.Title,
                MessagesJson = r.MessagesJson,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
            })
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    private static AgentChatSessionRow ToRow(AgentChatSession s) => new()
    {
        Id = s.Id,
        ProjectId = s.ProjectId,
        UserId = s.UserId,
        Title = s.Title,
        MessagesJson = JsonSerializer.Serialize(
            s.Messages.Select(m => new { m.Role, m.Content, Timestamp = m.Timestamp.ToUnixTimeMilliseconds() }).ToList(), Json),
        CreatedAt = s.CreatedAt,
        UpdatedAt = s.UpdatedAt,
    };

    private static AgentChatSession ToDomain(AgentChatSessionRow r)
    {
        var messages = string.IsNullOrEmpty(r.MessagesJson)
            ? new List<AgentMessageJson>()
            : JsonSerializer.Deserialize<List<AgentMessageJson>>(r.MessagesJson, Json)
              ?? new List<AgentMessageJson>();

        var domainMessages = messages
            .Select(m => new AgentMessage(m.Role, m.Content, DateTimeOffset.FromUnixTimeMilliseconds(m.Timestamp)))
            .ToList();

        return AgentChatSession.Rehydrate(
            r.Id, r.ProjectId, r.UserId, r.Title, domainMessages, r.CreatedAt, r.UpdatedAt);
    }

    private sealed record AgentMessageJson(string Role, string Content, long Timestamp);
}
