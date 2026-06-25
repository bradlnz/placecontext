using System.Text.Json;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfRequirementsRepository : IRequirementsRepository
{
    private static readonly Guid GlobalKey = Guid.Empty;

    private readonly AppDbContext _db;
    public EfRequirementsRepository(AppDbContext db) => _db = db;

    public async Task<Requirements?> GetGlobalAsync(CancellationToken ct = default)
    {
        var r = await _db.Requirements.AsNoTracking().FirstOrDefaultAsync(x => x.ProjectId == GlobalKey, ct);
        return r is null ? null : Requirements.Rehydrate(null, r.Markdown, r.CreatedAt, r.UpdatedAt);
    }

    public async Task<Requirements?> GetForProjectAsync(ProjectId projectId, CancellationToken ct = default)
    {
        var r = await _db.Requirements.AsNoTracking().FirstOrDefaultAsync(x => x.ProjectId == projectId.Value, ct);
        return r is null ? null : Requirements.Rehydrate(projectId, r.Markdown, r.CreatedAt, r.UpdatedAt);
    }

    public async Task SaveAsync(Requirements requirements, CancellationToken ct = default)
    {
        var key = requirements.ProjectId?.Value ?? GlobalKey;
        // Tenant-filtered (global query filter); composite PK is (TenantId, ProjectId), so no FindAsync.
        var row = await _db.Requirements.FirstOrDefaultAsync(x => x.ProjectId == key, ct);
        if (row is null)
            await _db.Requirements.AddAsync(new RequirementsRow
            {
                ProjectId = key, Markdown = requirements.Markdown,
                CreatedAt = requirements.CreatedAt, UpdatedAt = requirements.UpdatedAt
            }, ct);
        else
        {
            row.Markdown = requirements.Markdown;
            row.UpdatedAt = requirements.UpdatedAt;
        }
    }
}
