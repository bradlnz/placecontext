using System.Text.Json;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfJobRepository : IJobRepository
{
    private readonly AppDbContext _db;
    private readonly IDataEncryptor _enc;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private static string P => IDataEncryptor.Purpose.JobSource;

    public EfJobRepository(AppDbContext db, IDataEncryptor enc) => (_db, _enc) = (db, enc);

    public async Task AddAsync(Job job, CancellationToken ct = default)
        => await _db.Jobs.AddAsync(ToRow(job), ct);

    public async Task UpdateAsync(Job job, CancellationToken ct = default)
    {
        var existing = await _db.Jobs.FindAsync(new object[] { job.Id }, ct);
        if (existing is null) return;

        var updated = ToRow(job);
        existing.Name = updated.Name;
        existing.Description = updated.Description;
        existing.MapSourceKind = updated.MapSourceKind;
        existing.MapImage = updated.MapImage;
        existing.MapRuntimeId = updated.MapRuntimeId;
        existing.MapSource = updated.MapSource;
        existing.MapFilesJson = updated.MapFilesJson;
        existing.MapEntrypoint = updated.MapEntrypoint;
        existing.InputPayloadsJson = updated.InputPayloadsJson;
        existing.MapEnvJson = updated.MapEnvJson;
        existing.ReduceSourceKind = updated.ReduceSourceKind;
        existing.ReduceImage = updated.ReduceImage;
        existing.ReduceRuntimeId = updated.ReduceRuntimeId;
        existing.ReduceSource = updated.ReduceSource;
        existing.ReduceFilesJson = updated.ReduceFilesJson;
        existing.ReduceEntrypoint = updated.ReduceEntrypoint;
        existing.ReduceEnvJson = updated.ReduceEnvJson;
        existing.SuccessCodesJson = updated.SuccessCodesJson;
        existing.PartialCodesJson = updated.PartialCodesJson;
        existing.ConcurrencyLimit = updated.ConcurrencyLimit;
        existing.ParametersJson = updated.ParametersJson;
        existing.AllowNetworkEgress = updated.AllowNetworkEgress;
        existing.TimeoutSeconds = updated.TimeoutSeconds;
        existing.PostJobActionsJson = updated.PostJobActionsJson;
        existing.ReturnType = updated.ReturnType;
        existing.ReturnFileName = updated.ReturnFileName;
        existing.RetryCount = updated.RetryCount;
        existing.RetryDelaySeconds = updated.RetryDelaySeconds;
        existing.McpConnectionIdsJson = updated.McpConnectionIdsJson;
        existing.UpdatedAt = updated.UpdatedAt;
    }

    public async Task RemoveAsync(Guid jobId, CancellationToken ct = default)
    {
        var existing = await _db.Jobs.FindAsync(new object[] { jobId }, ct);
        if (existing is not null) _db.Jobs.Remove(existing);
    }

    public async Task<Job?> GetByIdAsync(Guid jobId, CancellationToken ct = default)
    {
        var row = await _db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<Job>> ListForProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        var rows = await _db.Jobs.AsNoTracking()
            .Where(j => j.ProjectId == projectId)
            .OrderBy(j => j.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    private JobRow ToRow(Job job)
    {
        var row = new JobRow
        {
            Id = job.Id,
            ProjectId = job.ProjectId,
            Name = job.Name,
            Description = job.Description,
            InputPayloadsJson = Enc(JsonSerializer.Serialize(job.MapSpec.InputPayloads ?? Array.Empty<string>(), Json))!,
            MapEnvJson = Enc(JsonSerializer.Serialize(job.MapSpec.Env ?? new Dictionary<string, string>(), Json))!,
            SuccessCodesJson = JsonSerializer.Serialize(job.ExitCodePolicy.SuccessCodes.ToList(), Json),
            PartialCodesJson = JsonSerializer.Serialize(job.ExitCodePolicy.PartialCodes.ToList(), Json),
            ConcurrencyLimit = job.ConcurrencyLimit,
            ParametersJson = JsonSerializer.Serialize(
                job.Parameters.Select(p => new JobParameterJson(p.Name, p.Label, p.Required, p.Type, p.Options.ToList())).ToList(), Json),
            AllowNetworkEgress = job.AllowNetworkEgress,
            TimeoutSeconds = job.TimeoutSeconds,
            PostJobActionsJson = JsonSerializer.Serialize(job.PostJobActions.Select(a => a.ToString()).ToList(), Json),
            ReturnType = job.ReturnType.ToString(),
            ReturnFileName = job.ReturnFileName,
            RetryCount = job.RetryCount,
            RetryDelaySeconds = job.RetryDelaySeconds,
            McpConnectionIdsJson = JsonSerializer.Serialize(job.McpConnectionIds.ToList(), Json),
            CreatedAt = job.CreatedAt,
            UpdatedAt = job.UpdatedAt,
        };

        (row.MapSourceKind, row.MapImage, row.MapRuntimeId, row.MapSource, row.MapFilesJson, row.MapEntrypoint)
            = SerialiseSource(job.MapSpec.Source);
        row.MapSource = Enc(row.MapSource);
        row.MapFilesJson = Enc(row.MapFilesJson);

        if (job.ReduceSpec is not null)
        {
            var (kind, img, rtId, src, filesJson, entry) = SerialiseSource(job.ReduceSpec.Source);
            row.ReduceSourceKind = kind;
            row.ReduceImage = img;
            row.ReduceRuntimeId = rtId;
            row.ReduceSource = Enc(src);
            row.ReduceFilesJson = Enc(filesJson);
            row.ReduceEntrypoint = entry;
            row.ReduceEnvJson = Enc(JsonSerializer.Serialize(job.ReduceSpec.Env, Json));
        }

        return row;
    }

    private string? Enc(string? s) => s is null ? null : _enc.Protect(s, P);
    private string Dec(string? s) => _enc.Unprotect(s, P);

    private static (string Kind, string? Image, string? RuntimeId, string? Source, string? FilesJson, string? Entrypoint)
        SerialiseSource(WorkloadSource source) => source switch
        {
            WorkloadSource.ImageWorkload img => ("image", img.Image, null, null, null, null),
            WorkloadSource.CodeWorkload code => (
                "code", null, code.RuntimeId,
                code.Source, // legacy single-source column kept populated with the entry file for back-compat
                JsonSerializer.Serialize(code.Files.Select(f => new CodeFileJson(f.Path, f.Content)).ToList(), Json),
                code.Entrypoint),
            _ => throw new InvalidOperationException($"Unknown WorkloadSource type: {source.GetType().Name}"),
        };

    private Job ToDomain(JobRow row)
    {
        var inputPayloads = JsonSerializer.Deserialize<List<string>>(Dec(row.InputPayloadsJson), Json)
            ?? new List<string>();
        var mapEnv = JsonSerializer.Deserialize<Dictionary<string, string>>(Dec(row.MapEnvJson), Json)
            ?? new Dictionary<string, string>();
        var successCodes = JsonSerializer.Deserialize<List<int>>(row.SuccessCodesJson, Json)
            ?? new List<int> { 0 };
        var partialCodes = JsonSerializer.Deserialize<List<int>>(row.PartialCodesJson, Json)
            ?? new List<int>();

        var mapSource = DeserialiseSource("map", row.MapSourceKind,
            row.MapImage, row.MapRuntimeId, Dec(row.MapSource), Dec(row.MapFilesJson), row.MapEntrypoint);
        var mapSpec = new MapSpec(mapSource, inputPayloads, mapEnv);

        ReduceSpec? reduceSpec = null;
        if (row.ReduceSourceKind is not null)
        {
            var reduceSource = DeserialiseSource("reduce", row.ReduceSourceKind,
                row.ReduceImage, row.ReduceRuntimeId, Dec(row.ReduceSource), Dec(row.ReduceFilesJson), row.ReduceEntrypoint);
            var reduceEnv = row.ReduceEnvJson is not null
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(Dec(row.ReduceEnvJson), Json)
                    ?? new Dictionary<string, string>()
                : new Dictionary<string, string>();
            reduceSpec = new ReduceSpec(reduceSource, reduceEnv);
        }

        var policy = new ExitCodePolicy(successCodes, partialCodes);

        var parameters = (JsonSerializer.Deserialize<List<JobParameterJson>>(row.ParametersJson, Json)
                ?? new List<JobParameterJson>())
            .Select(p => new JobParameter(p.Name, p.Label, p.Required, p.Type, p.Options))
            .ToList();

        var postJobActions = (JsonSerializer.Deserialize<List<string>>(row.PostJobActionsJson, Json)
                ?? new List<string>())
            .Select(s => Enum.TryParse<PostJobActionKind>(s, out var k) ? (PostJobActionKind?)k : null)
            .Where(k => k is not null).Select(k => k!.Value)
            .ToList();

        var returnType = Enum.TryParse<JobReturnType>(row.ReturnType, out var rt) ? rt : JobReturnType.Json;

        var mcpConnectionIds = string.IsNullOrWhiteSpace(row.McpConnectionIdsJson)
            ? new List<Guid>()
            : (JsonSerializer.Deserialize<List<Guid>>(row.McpConnectionIdsJson, Json) ?? new List<Guid>());

        return Job.Rehydrate(
            row.Id, row.ProjectId, row.Name, row.Description,
            mapSpec, reduceSpec, row.ConcurrencyLimit, policy, row.CreatedAt, row.UpdatedAt,
            allowNetworkEgress: row.AllowNetworkEgress, parameters: parameters,
            timeoutSeconds: row.TimeoutSeconds, postJobActions: postJobActions,
            returnType: returnType, returnFileName: row.ReturnFileName,
            retryCount: row.RetryCount, retryDelaySeconds: row.RetryDelaySeconds,
            mcpConnectionIds: mcpConnectionIds);
    }

    private static WorkloadSource DeserialiseSource(
        string context,
        string kind,
        string? image, string? runtimeId, string? source, string? filesJson, string? entrypoint)
        => kind switch
        {
            "image" => new WorkloadSource.ImageWorkload(
                image ?? throw new InvalidOperationException($"{context} image is null for ImageWorkload row.")),
            "code" => DeserialiseCode(context,
                runtimeId ?? throw new InvalidOperationException($"{context} runtimeId is null for CodeWorkload row."),
                source, filesJson, entrypoint),
            _ => throw new InvalidOperationException($"Unknown WorkloadSource kind '{kind}' in {context} row."),
        };

    /// <summary>
    /// Prefers the multi-file JSON column; falls back to the legacy single-source column (via the
    /// single-file CodeWorkload constructor, which honours the entrypoint) for rows written before the
    /// file-set migration.
    /// </summary>
    private static WorkloadSource DeserialiseCode(
        string context, string runtimeId, string? legacySource, string? filesJson, string? entrypoint)
    {
        if (!string.IsNullOrWhiteSpace(filesJson))
        {
            var files = JsonSerializer.Deserialize<List<CodeFileJson>>(filesJson, Json);
            if (files is { Count: > 0 })
                return new WorkloadSource.CodeWorkload(
                    runtimeId, files.Select(f => new CodeFile(f.Path, f.Content)).ToList(), entrypoint);
        }

        if (!string.IsNullOrWhiteSpace(legacySource))
            return new WorkloadSource.CodeWorkload(runtimeId, legacySource!, entrypoint);

        throw new InvalidOperationException($"{context} code workload has neither files nor source.");
    }

    private sealed record CodeFileJson(string Path, string Content);
    private sealed record JobParameterJson(string Name, string? Label, bool Required, string Type = "text", List<string>? Options = null);
}
