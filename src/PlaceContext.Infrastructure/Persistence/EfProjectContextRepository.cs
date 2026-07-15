using System.Text.Json;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfProjectContextRepository : IProjectContextRepository
{
    private readonly AppDbContext _db;
    private readonly IDataEncryptor _enc;
    private static string P => IDataEncryptor.Purpose.Context;

    public EfProjectContextRepository(AppDbContext db, IDataEncryptor enc) => (_db, _enc) = (db, enc);

    public async Task<ProjectContext?> GetForProjectAsync(ProjectId projectId, CancellationToken ct = default)
    {
        var r = await _db.ProjectContexts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProjectId == projectId.Value, ct);
        return r is null ? null : ProjectContext.Rehydrate(projectId, _enc.Unprotect(r.Markdown, P), r.CreatedAt, r.UpdatedAt);
    }

    public async Task SaveAsync(ProjectContext context, CancellationToken ct = default)
    {
        var row = await _db.ProjectContexts.FindAsync(new object[] { context.ProjectId.Value }, ct);
        var cipher = _enc.Protect(context.Markdown, P);
        if (row is null)
            await _db.ProjectContexts.AddAsync(new ProjectContextRow
            {
                ProjectId = context.ProjectId.Value, Markdown = cipher,
                CreatedAt = context.CreatedAt, UpdatedAt = context.UpdatedAt
            }, ct);
        else
        {
            row.Markdown = cipher;
            row.UpdatedAt = context.UpdatedAt;
        }
    }
}
