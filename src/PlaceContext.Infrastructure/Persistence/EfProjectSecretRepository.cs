using Microsoft.EntityFrameworkCore;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfProjectSecretRepository : IProjectSecretRepository
{
    private readonly AppDbContext _db;
    public EfProjectSecretRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<(string Name, DateTimeOffset CreatedAt)>> ListAsync(Guid projectId, CancellationToken ct = default)
        => await _db.JobSecrets.Where(s => s.ProjectId == projectId).OrderBy(s => s.Name)
            .Select(s => new ValueTuple<string, DateTimeOffset>(s.Name, s.CreatedAt)).ToListAsync(ct);

    public async Task AddAsync(Guid projectId, string name, string cipher, DateTimeOffset now, CancellationToken ct = default)
    {
        if (await _db.JobSecrets.AnyAsync(s => s.ProjectId == projectId && s.Name == name, ct))
            throw new InvalidOperationException($"Secret '{name}' already exists — delete it first to replace it.");
        _db.JobSecrets.Add(new JobSecretRow { ProjectId = projectId, Name = name, Cipher = cipher, CreatedAt = now });
    }

    public async Task DeleteAsync(Guid projectId, string name, CancellationToken ct = default)
    {
        var row = await _db.JobSecrets.FirstOrDefaultAsync(s => s.ProjectId == projectId && s.Name == name, ct);
        if (row is not null) _db.JobSecrets.Remove(row);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetCiphersAsync(Guid projectId, CancellationToken ct = default)
        => await _db.JobSecrets.Where(s => s.ProjectId == projectId).ToDictionaryAsync(s => s.Name, s => s.Cipher, ct);
}
