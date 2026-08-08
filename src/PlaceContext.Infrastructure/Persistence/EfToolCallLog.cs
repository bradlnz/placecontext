using PlaceContext.Application.Ports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace PlaceContext.Infrastructure.Persistence;

/// <summary>
/// EF-backed MCP tool-call log. A singleton shared by the MCP host (writer) and the portal (reader),
/// which may run as separate processes against the same Postgres database. Each operation opens a
/// short-lived scoped <see cref="AppDbContext"/>, so it never holds the request-scoped context.
/// Request/response JSON and summaries are encrypted at rest.
/// </summary>
public sealed class EfToolCallLog : IToolCallLog
{
    private const int Capacity = 200;
    private readonly IServiceScopeFactory _scopes;
    private readonly IDataEncryptor _enc;
    private static string P => DataEncryptionPurpose.ToolCall;

    public EfToolCallLog(IServiceScopeFactory scopes, IDataEncryptor enc)
    {
        _scopes = scopes;
        _enc = enc;
    }

    public void Record(ToolCallEntry e)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ToolCalls.Add(new ToolCallRow
            {
                Id = e.Id, Tool = e.Tool, Direction = e.Direction, Project = e.Project,
                Summary = _enc.Protect(e.Summary, P),
                Status = e.Status.ToString(), DurationMs = e.DurationMs,
                RequestJson = _enc.Protect(e.RequestJson, P),
                ResponseJson = _enc.Protect(e.ResponseJson, P),
                At = e.At
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
                    r.Id, r.Tool, r.Direction, r.Project,
                    _enc.Unprotect(r.Summary, P),
                    Enum.Parse<ToolCallStatus>(r.Status), r.DurationMs,
                    _enc.Unprotect(r.RequestJson, P),
                    _enc.Unprotect(r.ResponseJson, P), r.At))
                .ToList();
        }
        catch { return Array.Empty<ToolCallEntry>(); }
    }
}
