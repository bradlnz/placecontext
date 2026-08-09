using System.Text.Json;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Projects.Infrastructure.Persistence;

public sealed class EfProjectRepository : IProjectRepository
{
    private readonly ProjectsDbContext _db;
    public EfProjectRepository(ProjectsDbContext db) => _db = db;

    public async Task AddAsync(Project p, CancellationToken ct = default)
        => await _db.Projects.AddAsync(ToRow(p), ct);

    public async Task UpdateAsync(Project p, CancellationToken ct = default)
    {
        var row = await _db.Projects.FindAsync(new object[] { p.Id.Value }, ct);
        if (row is null) { await _db.Projects.AddAsync(ToRow(p), ct); return; }
        Map(p, row);
    }

    public async Task<Project?> GetByIdAsync(ProjectId id, CancellationToken ct = default)
    {
        var row = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<Project?> GetByPathAsync(ProjectPath path, CancellationToken ct = default)
    {
        var row = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(x => x.Path == path.Value, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<Project>> ListAsync(CancellationToken ct = default)
    {
        var rows = await _db.Projects.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    private static ProjectRow ToRow(Project p)
    {
        var row = new ProjectRow { Id = p.Id.Value, Path = p.Path.Value, DiscoveredAt = p.DiscoveredAt };
        Map(p, row);
        return row;
    }

    private static void Map(Project p, ProjectRow row)
    {
        row.Name = p.Name.Value;
        row.Status = p.Status.ToString();
        row.RegisteredAt = p.RegisteredAt;
        row.GraphJson = JsonCodec.EncodeSnapshot(p.LastGraph);
    }

    private static Project ToDomain(ProjectRow r) => Project.Rehydrate(
        ProjectId.From(r.Id), ProjectName.From(r.Name), ProjectPath.From(r.Path),
        Enum.Parse<ProjectStatus>(r.Status), r.DiscoveredAt, r.RegisteredAt,
        JsonCodec.DecodeSnapshot(r.GraphJson));
}
