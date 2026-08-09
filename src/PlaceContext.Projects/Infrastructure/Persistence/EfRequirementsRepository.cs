using System.Text.Json;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Projects.Infrastructure.Persistence;

public sealed class EfRequirementsRepository : IRequirementsRepository
{
    private static readonly Guid GlobalKey = Guid.Empty;

    private readonly ProjectsDbContext _db;
    private readonly IDataEncryptor _enc;
    private static string P => DataEncryptionPurpose.Requirements;

    public EfRequirementsRepository(ProjectsDbContext db, IDataEncryptor enc) => (_db, _enc) = (db, enc);

    public async Task<Requirements?> GetGlobalAsync(CancellationToken ct = default)
    {
        var r = await _db.Requirements.AsNoTracking().FirstOrDefaultAsync(x => x.ProjectId == GlobalKey, ct);
        return r is null ? null : Requirements.Rehydrate(null, _enc.Unprotect(r.Markdown, P), r.CreatedAt, r.UpdatedAt);
    }

    public async Task<Requirements?> GetForProjectAsync(ProjectId projectId, CancellationToken ct = default)
    {
        var r = await _db.Requirements.AsNoTracking().FirstOrDefaultAsync(x => x.ProjectId == projectId.Value, ct);
        return r is null ? null : Requirements.Rehydrate(projectId, _enc.Unprotect(r.Markdown, P), r.CreatedAt, r.UpdatedAt);
    }

    public async Task SaveAsync(Requirements requirements, CancellationToken ct = default)
    {
        var key = requirements.ProjectId?.Value ?? GlobalKey;
        // Tenant-filtered (global query filter); composite PK is (TenantId, ProjectId), so no FindAsync.
        var row = await _db.Requirements.FirstOrDefaultAsync(x => x.ProjectId == key, ct);
        var cipher = _enc.Protect(requirements.Markdown, P);
        if (row is null)
            await _db.Requirements.AddAsync(new RequirementsRow
            {
                ProjectId = key, Markdown = cipher,
                CreatedAt = requirements.CreatedAt, UpdatedAt = requirements.UpdatedAt
            }, ct);
        else
        {
            row.Markdown = cipher;
            row.UpdatedAt = requirements.UpdatedAt;
        }
    }
}
