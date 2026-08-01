using System.Text.Json;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfChainRunRepository : IChainRunRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;
    private readonly IDataEncryptor _encryptor;
    private static string Purpose => IDataEncryptor.Purpose.ChainRun;

    public EfChainRunRepository(AppDbContext db, IDataEncryptor encryptor)
        => (_db, _encryptor) = (db, encryptor);

    public async Task AddAsync(ChainRun run, CancellationToken ct = default)
        => await _db.ChainRuns.AddAsync(ToRow(run), ct);

    public async Task UpdateAsync(ChainRun run, CancellationToken ct = default)
    {
        var existing = await _db.ChainRuns.FindAsync(new object[] { run.Id }, ct);
        if (existing is null) return;

        existing.Status = run.Status.ToString();
        existing.StepsJson = Encrypt(JsonSerializer.Serialize(run.Steps.Select(StepJson.From), Json));
        existing.FinalOutput = EncryptNullable(run.FinalOutput);
        existing.FinishedAt = run.FinishedAt;
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
        StepsJson = Encrypt(JsonSerializer.Serialize(r.Steps.Select(StepJson.From), Json)),
        FinalOutput = EncryptNullable(r.FinalOutput),
        StartedAt = r.StartedAt,
        FinishedAt = r.FinishedAt,
    };

    private ChainRun ToDomain(ChainRunRow r)
    {
        var steps = (JsonSerializer.Deserialize<List<StepJson>>(Decrypt(r.StepsJson), Json) ?? new())
            .Select(s => s.ToDomain())
            .ToList();
        return ChainRun.Rehydrate(r.Id, r.ChainId, r.ProjectId, r.ChainName,
            Enum.Parse<ChainRunStatus>(r.Status), steps, DecryptNullable(r.FinalOutput),
            r.StartedAt, r.FinishedAt);
    }

    private string Encrypt(string value) => _encryptor.Protect(value, Purpose);
    private string Decrypt(string value) => _encryptor.Unprotect(value, Purpose);
    private string? EncryptNullable(string? value) => value is null ? null : Encrypt(value);
    private string? DecryptNullable(string? value) => value is null ? null : Decrypt(value);

    /// <summary>
    /// Wire shape of one persisted step. <c>StageIndex</c>/<c>BranchIndex</c> are nullable so rows
    /// written before fan-out existed (every step its own stage, no branch concept) still deserialize
    /// — <see cref="ToDomain"/> falls back to <c>StageIndex == Index, BranchIndex == 0</c>, exactly
    /// what a migrated linear chain's steps look like anyway. New rows always carry both explicitly
    /// (see the <c>AddChainRunStageIndex</c> migration, which also backfills existing rows).
    /// </summary>
    private sealed class StepJson
    {
        public int Index { get; set; }
        public int? StageIndex { get; set; }
        public int? BranchIndex { get; set; }
        public Guid JobId { get; set; }
        public string JobName { get; set; } = "";
        public Guid? RunId { get; set; }
        public string Status { get; set; } = "";
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
        public string? ActionType { get; set; }
        public string? Provider { get; set; }
        public string? ExternalId { get; set; }
        public string? Error { get; set; }

        public static StepJson From(ChainStepRun s) => new()
        {
            Index = s.Index,
            StageIndex = s.StageIndex,
            BranchIndex = s.BranchIndex,
            JobId = s.JobId,
            JobName = s.JobName,
            RunId = s.RunId,
            Status = s.Status.ToString(),
            StartedAt = s.StartedAt,
            FinishedAt = s.FinishedAt,
            ActionType = s.ActionType,
            Provider = s.Provider,
            ExternalId = s.ExternalId,
            Error = s.Error,
        };

        public ChainStepRun ToDomain() => new(
            Index, StageIndex ?? Index, BranchIndex ?? 0, JobId, JobName, RunId,
            Enum.Parse<ChainStepStatus>(Status), StartedAt, FinishedAt,
            ActionType, Provider, ExternalId, Error);
    }
}
