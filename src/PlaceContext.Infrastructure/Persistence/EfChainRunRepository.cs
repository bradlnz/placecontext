using System.Text.Json;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfChainRunRepository : IChainRunRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;

    public EfChainRunRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(ChainRun run, CancellationToken ct = default)
        => await _db.ChainRuns.AddAsync(ToRow(run), ct);

    public async Task UpdateAsync(ChainRun run, CancellationToken ct = default)
    {
        var existing = await _db.ChainRuns.FindAsync(new object[] { run.Id }, ct);
        if (existing is null) return;

        existing.Status = run.Status.ToString();
        existing.StepsJson = JsonSerializer.Serialize(run.Steps.Select(StepJson.From), Json);
        existing.FinalOutput = run.FinalOutput;
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

    private static ChainRunRow ToRow(ChainRun r) => new()
    {
        Id = r.Id,
        ChainId = r.ChainId,
        ProjectId = r.ProjectId,
        ChainName = r.ChainName,
        Status = r.Status.ToString(),
        StepsJson = JsonSerializer.Serialize(r.Steps.Select(StepJson.From), Json),
        FinalOutput = r.FinalOutput,
        StartedAt = r.StartedAt,
        FinishedAt = r.FinishedAt,
    };

    private static ChainRun ToDomain(ChainRunRow r)
    {
        var steps = (JsonSerializer.Deserialize<List<StepJson>>(r.StepsJson, Json) ?? new())
            .Select(s => s.ToDomain())
            .ToList();
        return ChainRun.Rehydrate(r.Id, r.ChainId, r.ProjectId, r.ChainName,
            Enum.Parse<ChainRunStatus>(r.Status), steps, r.FinalOutput, r.StartedAt, r.FinishedAt);
    }

    private sealed class StepJson
    {
        public int Index { get; set; }
        public Guid JobId { get; set; }
        public string JobName { get; set; } = "";
        public Guid? RunId { get; set; }
        public string Status { get; set; } = "";
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }

        public static StepJson From(ChainStepRun s) => new()
        {
            Index = s.Index,
            JobId = s.JobId,
            JobName = s.JobName,
            RunId = s.RunId,
            Status = s.Status.ToString(),
            StartedAt = s.StartedAt,
            FinishedAt = s.FinishedAt,
        };

        public ChainStepRun ToDomain() => new(
            Index, JobId, JobName, RunId, Enum.Parse<ChainStepStatus>(Status), StartedAt, FinishedAt);
    }
}
