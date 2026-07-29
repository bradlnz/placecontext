using Microsoft.EntityFrameworkCore;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfChatCommandRepository : IChatCommandRepository
{
    private readonly AppDbContext _db;

    public EfChatCommandRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<ChatCommand>> ListForProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        var rows = await _db.ChatCommands
            .Where(r => r.ProjectId == projectId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<ChatCommand?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.ChatCommands.FindAsync(new object[] { id }, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task AddAsync(ChatCommand command, CancellationToken ct = default)
    {
        _db.ChatCommands.Add(ToRow(command));
        await Task.CompletedTask;
    }

    public async Task UpdateAsync(ChatCommand command, CancellationToken ct = default)
    {
        var row = await _db.ChatCommands.FindAsync(new object[] { command.Id }, ct);
        if (row is not null)
        {
            row.Name = command.Name;
            row.Description = command.Description;
            row.ToolName = command.ToolName;
            row.Args = command.Args;
            row.UpdatedAt = command.UpdatedAt;
        }
    }

    public async Task RemoveAsync(Guid commandId, CancellationToken ct = default)
    {
        var row = await _db.ChatCommands.FindAsync(new object[] { commandId }, ct);
        if (row is not null) _db.ChatCommands.Remove(row);
    }

    private static ChatCommandRow ToRow(ChatCommand c) => new()
    {
        Id = c.Id,
        ProjectId = c.ProjectId,
        Name = c.Name,
        Description = c.Description,
        ToolName = c.ToolName,
        Args = c.Args,
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt,
    };

    private static ChatCommand ToDomain(ChatCommandRow r) =>
        ChatCommand.Rehydrate(r.Id, r.ProjectId, r.Name, r.Description,
            r.ToolName, r.Args, r.CreatedAt, r.UpdatedAt);
}
