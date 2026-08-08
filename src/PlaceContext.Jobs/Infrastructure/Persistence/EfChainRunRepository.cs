using System.Text.Json;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Jobs.Infrastructure.Persistence;

public sealed class EfChainRunRepository : IChainRunRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;
    private readonly IDataEncryptor _encryptor;
    private static string Purpose => DataEncryptionPurpose.ChainRun;

    public EfChainRunRepository(AppDbContext db, IDataEncryptor encryptor)
        => (_db, _encryptor) = (db, encryptor);

    public async Task AddAsync(ChainRun run, CancellationToken ct = default)
        => await _db.ChainRuns.AddAsync(ToRow(run), ct);

    public async Task UpdateAsync(ChainRun run, CancellationToken ct = default)
    {
        var existing = await _db.ChainRuns.FindAsync(new object[] { run.Id }, ct);
        if (existing is null) return;

        existing.Status = run.Status.ToString();
        existing.StepsJson = Encrypt(JsonSerializer.Serialize(run.Steps.Select(ChainRunStepJson.From), Json));
        existing.FinalOutput = EncryptNullable(run.FinalOutput);
        existing.FinishedAt = run.FinishedAt;
        existing.ResumeAt = run.ResumeAt;
        existing.ResumeStageIndex = run.ResumeStageIndex;
        existing.CrmClientId = run.CrmClientId;
        existing.ContinuationOverrides = EncryptNullable(run.ContinuationOverridesJson);
        if (run.Status != ChainRunStatus.Running)
        {
            existing.ContinuationClaimedAt = null;
            existing.ContinuationClaimedBy = null;
        }
    }

    public async Task<ChainRun?> GetByIdAsync(Guid chainRunId, CancellationToken ct = default)
    {
        var row = await _db.ChainRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == chainRunId, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<ChainRun>> ListForChainAsync(Guid chainId, int take, CancellationToken ct = default)
    {
        var rows = await _db.ChainRuns.AsNoTracking()
            .Where(r => r.ChainId == chainId)
            .OrderByDescending(r => r.StartedAt)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<ChainRun>> ListRecentAsync(int take, CancellationToken ct = default)
    {
        var rows = await _db.ChainRuns.AsNoTracking()
            .OrderByDescending(r => r.StartedAt)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    private ChainRunRow ToRow(ChainRun r) => new()
    {
        Id = r.Id,
        ChainId = r.ChainId,
        ProjectId = r.ProjectId,
        ChainName = r.ChainName,
        Status = r.Status.ToString(),
        StepsJson = Encrypt(JsonSerializer.Serialize(r.Steps.Select(ChainRunStepJson.From), Json)),
        FinalOutput = EncryptNullable(r.FinalOutput),
        StartedAt = r.StartedAt,
        FinishedAt = r.FinishedAt,
        ResumeAt = r.ResumeAt,
        ResumeStageIndex = r.ResumeStageIndex,
        CrmClientId = r.CrmClientId,
        ContinuationOverrides = EncryptNullable(r.ContinuationOverridesJson),
    };

    private ChainRun ToDomain(ChainRunRow r)
    {
        var steps = (JsonSerializer.Deserialize<List<ChainRunStepJson>>(Decrypt(r.StepsJson), Json) ?? new())
            .Select(s => s.ToDomain())
            .ToList();
        return ChainRun.Rehydrate(r.Id, r.ChainId, r.ProjectId, r.ChainName,
            Enum.Parse<ChainRunStatus>(r.Status), steps, DecryptNullable(r.FinalOutput),
            r.StartedAt, r.FinishedAt, r.ResumeAt, r.ResumeStageIndex, r.CrmClientId,
            DecryptNullable(r.ContinuationOverrides));
    }

    private string Encrypt(string value) => _encryptor.Protect(value, Purpose);
    private string Decrypt(string value) => _encryptor.Unprotect(value, Purpose);
    private string? EncryptNullable(string? value) => value is null ? null : Encrypt(value);
    private string? DecryptNullable(string? value) => value is null ? null : Decrypt(value);

}
