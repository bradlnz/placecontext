using System.Text.Json;
using System.Text.Json.Serialization;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfJobRunRepository : IJobRunRepository
{
    private readonly AppDbContext _db;
    private readonly IDataEncryptor _enc;
    private static string P => IDataEncryptor.Purpose.JobRun;
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public EfJobRunRepository(AppDbContext db, IDataEncryptor enc) => (_db, _enc) = (db, enc);

    public async Task AddAsync(JobRun run, CancellationToken ct = default)
        => await _db.JobRuns.AddAsync(ToRow(run), ct);

    public async Task UpdateAsync(JobRun run, CancellationToken ct = default)
    {
        var existing = await _db.JobRuns.FindAsync(new object[] { run.Id }, ct);
        if (existing is null) return;

        var updated = ToRow(run);
        existing.Status = updated.Status;
        existing.FinishedAt = updated.FinishedAt;
        existing.ShardResultsJson = updated.ShardResultsJson;
        existing.ReduceResultJson = updated.ReduceResultJson;
        // SnapshotJson is immutable (set once at AddAsync) — not updated here.
    }

    public async Task<JobRun?> GetByIdAsync(Guid runId, CancellationToken ct = default)
    {
        var row = await _db.JobRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<JobRun>> ListForJobAsync(Guid jobId, CancellationToken ct = default)
    {
        var rows = await _db.JobRuns.AsNoTracking()
            .Where(r => r.JobId == jobId)
            .OrderByDescending(r => r.StartedAt)
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<JobRun>> ListRecentAsync(int take, CancellationToken ct = default)
    {
        // Cross-job, newest first; tenant isolation comes from the global query filter on JobRuns.
        var rows = await _db.JobRuns.AsNoTracking()
            .OrderByDescending(r => r.StartedAt)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    private JobRunRow ToRow(JobRun run) => new()
    {
        Id = run.Id,
        JobId = run.JobId,
        ProjectId = run.ProjectId,
        Status = run.Status.ToString(),
        StartedAt = run.StartedAt,
        FinishedAt = run.FinishedAt,
        ShardResultsJson = Enc(JsonSerializer.Serialize(
            run.ShardResults.Select(s => new ShardResultJson
            {
                Index = s.Index,
                ExitCode = s.ExitCode,
                Outcome = s.Outcome.ToString(),
                Artifact = s.Artifact,
                Log = s.Log,
                Artifacts = ToArtifactsJson(s.Artifacts),
            }).ToList(), Json)),
        ReduceResultJson = run.ReduceResult is { } r
            ? Enc(JsonSerializer.Serialize(new ReduceResultJson
            {
                ExitCode = r.ExitCode,
                Succeeded = r.Succeeded,
                Artifact = r.Artifact,
                Log = r.Log,
                Artifacts = ToArtifactsJson(r.Artifacts),
            }, Json))
            : null,
        SnapshotJson = Enc(JsonSerializer.Serialize(ToSnapshotJson(run.Snapshot), Json)),
    };

    private string Enc(string s) => _enc.Protect(s, P);
    private string Dec(string? s) => _enc.Unprotect(s, P);

    private JobRun ToDomain(JobRunRow row)
    {
        var shards = JsonSerializer.Deserialize<List<ShardResultJson>>(Dec(row.ShardResultsJson), Json)
            ?? new List<ShardResultJson>();

        var shardResults = shards.Select(s => new ShardResult(
            index: s.Index,
            exitCode: s.ExitCode,
            outcome: Enum.TryParse<WorkloadOutcome>(s.Outcome, out var o) ? o : WorkloadOutcome.Failed,
            artifact: s.Artifact,
            log: s.Log,
            artifacts: FromArtifactsJson(s.Artifacts)));

        ReduceResult? reduceResult = null;
        if (row.ReduceResultJson is not null)
        {
            var rj = JsonSerializer.Deserialize<ReduceResultJson>(Dec(row.ReduceResultJson), Json);
            if (rj is not null)
                reduceResult = new ReduceResult(rj.ExitCode, rj.Succeeded, rj.Artifact, rj.Log,
                    FromArtifactsJson(rj.Artifacts));
        }

        var status = Enum.TryParse<JobRunStatus>(row.Status, out var st) ? st : JobRunStatus.Failed;

        var snapshot = DeserialiseSnapshot(Dec(row.SnapshotJson));

        return JobRun.Rehydrate(row.Id, row.JobId, row.ProjectId, status,
            row.StartedAt, row.FinishedAt, shardResults, reduceResult, snapshot);
    }

    // ── Artifact serialisation ────────────────────────────────────────────────────────────────────

    private static List<ArtifactJson> ToArtifactsJson(IReadOnlyList<RunArtifact> artifacts)
        => artifacts.Select(a => new ArtifactJson { Name = a.Name, Content = a.Content, IsBinary = a.IsBinary ? true : null }).ToList();

    private static IReadOnlyList<RunArtifact> FromArtifactsJson(List<ArtifactJson>? artifacts)
        => artifacts is null or { Count: 0 }
            ? Array.Empty<RunArtifact>()
            : artifacts.Select(a => new RunArtifact(a.Name, a.Content, a.IsBinary ?? false)).ToList();

    // ── Snapshot serialisation ────────────────────────────────────────────────────────────────────

    private static SnapshotJson ToSnapshotJson(WorkloadSnapshot snap) => new()
    {
        MapSource = ToSourceJson(snap.MapSource),
        InputPayloads = snap.InputPayloads.ToList(),
        MapEnv = new Dictionary<string, string>(snap.MapEnv),
        ReduceSource = snap.ReduceSource is not null ? ToSourceJson(snap.ReduceSource) : null,
        ReduceEnv = snap.ReduceEnv is not null ? new Dictionary<string, string>(snap.ReduceEnv) : null,
        ConcurrencyLimit = snap.ConcurrencyLimit,
        AllowNetworkEgress = snap.AllowNetworkEgress,
    };

    private static WorkloadSnapshot DeserialiseSnapshot(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
        {
            // Legacy rows (before snapshot was added) — synthesise a minimal default snapshot.
            var fallbackMap = new WorkloadSource.ImageWorkload("unknown");
            return new WorkloadSnapshot(fallbackMap, Array.Empty<string>(),
                new Dictionary<string, string>(), null, null, 1);
        }

        var sj = JsonSerializer.Deserialize<SnapshotJson>(json, Json) ?? new SnapshotJson();
        var mapSource = DeserialiseSourceJson(sj.MapSource);
        var reduceSource = sj.ReduceSource is not null ? DeserialiseSourceJson(sj.ReduceSource) : null;
        return new WorkloadSnapshot(
            mapSource,
            sj.InputPayloads ?? new List<string>(),
            sj.MapEnv ?? new Dictionary<string, string>(),
            reduceSource,
            sj.ReduceEnv,
            sj.ConcurrencyLimit < 1 ? 1 : sj.ConcurrencyLimit,
            allowNetworkEgress: sj.AllowNetworkEgress);
    }

    private static SourceJson ToSourceJson(WorkloadSource source) => source switch
    {
        WorkloadSource.ImageWorkload img => new SourceJson { Kind = "image", Image = img.Image },
        WorkloadSource.CodeWorkload code => new SourceJson
        {
            Kind = "code", RuntimeId = code.RuntimeId, Source = code.Source, Entrypoint = code.Entrypoint,
            Files = code.Files.Select(f => new CodeFileJson { Path = f.Path, Content = f.Content }).ToList(),
        },
        _ => throw new InvalidOperationException($"Unknown WorkloadSource: {source.GetType().Name}"),
    };

    private static WorkloadSource DeserialiseSourceJson(SourceJson sj) => sj.Kind switch
    {
        "image" => new WorkloadSource.ImageWorkload(sj.Image ?? "unknown"),
        "code" => sj.Files is { Count: > 0 }
            ? new WorkloadSource.CodeWorkload(
                sj.RuntimeId ?? "unknown",
                sj.Files.Select(f => new CodeFile(f.Path, f.Content)).ToList(),
                sj.Entrypoint)
            : new WorkloadSource.CodeWorkload(sj.RuntimeId ?? "unknown", sj.Source ?? "", sj.Entrypoint),
        _ => new WorkloadSource.ImageWorkload(sj.Image ?? "unknown"),
    };

    // ── JSON DTOs (private to this class) ────────────────────────────────────────────────────────

    private sealed class ShardResultJson
    {
        public int Index { get; set; }
        public int ExitCode { get; set; }
        public string Outcome { get; set; } = "";
        public string? Artifact { get; set; }
        public string? Log { get; set; }
        public List<ArtifactJson>? Artifacts { get; set; }
    }

    private sealed class ReduceResultJson
    {
        public int ExitCode { get; set; }
        public bool Succeeded { get; set; }
        public string? Artifact { get; set; }
        public string? Log { get; set; }
        public List<ArtifactJson>? Artifacts { get; set; }
    }

    private sealed class ArtifactJson
    {
        public string Name { get; set; } = "";
        public string Content { get; set; } = "";
        // Null (absent in JSON) for text artifacts — pre-existing rows keep deserializing unchanged.
        public bool? IsBinary { get; set; }
    }

    private sealed class SnapshotJson
    {
        public SourceJson MapSource { get; set; } = new();
        public List<string>? InputPayloads { get; set; }
        public Dictionary<string, string>? MapEnv { get; set; }
        public SourceJson? ReduceSource { get; set; }
        public Dictionary<string, string>? ReduceEnv { get; set; }
        public int ConcurrencyLimit { get; set; } = 1;
        /// <summary>Defaults to false for legacy rows (before AllowNetworkEgress was added).</summary>
        public bool AllowNetworkEgress { get; set; }
    }

    private sealed class SourceJson
    {
        public string Kind { get; set; } = "image";
        // ImageWorkload fields
        public string? Image { get; set; }
        // CodeWorkload fields
        public string? RuntimeId { get; set; }
        /// <summary>Legacy single-file content; superseded by <see cref="Files"/>.</summary>
        public string? Source { get; set; }
        public string? Entrypoint { get; set; }
        public List<CodeFileJson>? Files { get; set; }
    }

    private sealed class CodeFileJson
    {
        public string Path { get; set; } = "";
        public string Content { get; set; } = "";
    }
}
