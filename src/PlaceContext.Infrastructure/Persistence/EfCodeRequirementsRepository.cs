using System.Text.Json;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfCodeRequirementsRepository : ICodeRequirementsRepository
{
    private static readonly Guid GlobalKey = Guid.Empty;

    private readonly AppDbContext _db;
    public EfCodeRequirementsRepository(AppDbContext db) => _db = db;

    public async Task<CodeRequirements?> GetGlobalAsync(CancellationToken ct = default)
    {
        var r = await _db.CodeRequirements.AsNoTracking().FirstOrDefaultAsync(x => x.ProjectId == GlobalKey, ct);
        return r is null ? null : CodeRequirements.Rehydrate(null, r.Markdown, r.CreatedAt, r.UpdatedAt);
    }

    public async Task<CodeRequirements?> GetForProjectAsync(ProjectId projectId, CancellationToken ct = default)
    {
        var r = await _db.CodeRequirements.AsNoTracking().FirstOrDefaultAsync(x => x.ProjectId == projectId.Value, ct);
        return r is null ? null : CodeRequirements.Rehydrate(projectId, r.Markdown, r.CreatedAt, r.UpdatedAt);
    }

    public async Task SaveAsync(CodeRequirements requirements, CancellationToken ct = default)
    {
        var key = requirements.ProjectId?.Value ?? GlobalKey;
        // Tenant-filtered (global query filter); composite PK is (TenantId, ProjectId), so no FindAsync.
        var row = await _db.CodeRequirements.FirstOrDefaultAsync(x => x.ProjectId == key, ct);
        if (row is null)
            await _db.CodeRequirements.AddAsync(new CodeRequirementsRow
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
