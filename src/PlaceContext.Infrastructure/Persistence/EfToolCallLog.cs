using PlaceContext.Application.Ports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace PlaceContext.Infrastructure.Persistence;

/// <summary>
/// EF-backed MCP tool-call log. A singleton shared by the MCP host (writer) and the portal (reader),
/// which may run as separate processes against the same Postgres database. Each operation opens a
/// short-lived scoped <see cref="AppDbContext"/>, so it never holds the request-scoped context.
/// </summary>
public sealed class EfToolCallLog : IToolCallLog
{
    private const int Capacity = 200;
    private readonly IServiceScopeFactory _scopes;

    public EfToolCallLog(IServiceScopeFactory scopes) => _scopes = scopes;

    public void Record(ToolCallEntry e)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ToolCalls.Add(new ToolCallRow
            {
                Id = e.Id, Tool = e.Tool, Direction = e.Direction, Project = e.Project, Summary = e.Summary,
                Status = e.Status.ToString(), DurationMs = e.DurationMs,
                RequestJson = e.RequestJson, ResponseJson = e.ResponseJson, At = e.At
            });
            db.SaveChanges();

            // Prune to the most recent Capacity rows.
            var keep = db.ToolCalls.OrderByDescending(t => t.At).Select(t => t.Id).Take(Capacity);
            db.ToolCalls.Where(t => !keep.Contains(t.Id)).ExecuteDelete();
        }
        catch { /* logging must never break a tool call */ }
    }

    public IReadOnlyList<ToolCallEntry> Recent(int take = 100)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return db.ToolCalls.AsNoTracking().OrderByDescending(t => t.At).Take(take)
                .ToList()
                .Select(r => new ToolCallEntry(
                    r.Id, r.Tool, r.Direction, r.Project, r.Summary,
                    Enum.Parse<ToolCallStatus>(r.Status), r.DurationMs,
                    r.RequestJson, r.ResponseJson, r.At))
                .ToList();
        }
        catch { return Array.Empty<ToolCallEntry>(); }
    }
}
