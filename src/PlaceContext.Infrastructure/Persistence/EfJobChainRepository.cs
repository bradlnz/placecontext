using System.Text.Json;
using System.Text.Json.Serialization;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfJobChainRepository : IJobChainRepository
{
    private readonly AppDbContext _db;

    public EfJobChainRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(JobChain chain, CancellationToken ct = default)
        => await _db.JobChains.AddAsync(ToRow(chain), ct);

    public async Task UpdateAsync(JobChain chain, CancellationToken ct = default)
    {
        var existing = await _db.JobChains.FindAsync(new object[] { chain.Id }, ct);
        if (existing is null) return;

        existing.Name = chain.Name;
        existing.Description = chain.Description;
        existing.StagesJson = SerializeStages(chain.Stages);
        existing.UpdatedAt = chain.UpdatedAt;
    }

    public async Task RemoveAsync(Guid chainId, CancellationToken ct = default)
    {
        var existing = await _db.JobChains.FindAsync(new object[] { chainId }, ct);
        if (existing is not null) _db.JobChains.Remove(existing);
    }

    public async Task<JobChain?> GetByIdAsync(Guid chainId, CancellationToken ct = default)
    {
        var row = await _db.JobChains.AsNoTracking().FirstOrDefaultAsync(c => c.Id == chainId, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<JobChain>> ListForProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        var rows = await _db.JobChains.AsNoTracking()
            .Where(c => c.ProjectId == projectId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    private static JobChainRow ToRow(JobChain c) => new()
    {
        Id = c.Id,
        ProjectId = c.ProjectId,
        Name = c.Name,
        Description = c.Description,
        StagesJson = SerializeStages(c.Stages),
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt,
    };

    private static readonly JsonSerializerOptions GateJsonOptions = new()
    {
        WriteIndented = false,
    };

    private static string SerializeStages(IReadOnlyList<ChainStage> stages)
        => JsonSerializer.Serialize(stages.Select(SerializeStage));

    /// <summary>Serializes a single stage: as a job-id array when there's no gate or else-branch
    /// (backward compatible), or as an object when gates/else-branch are present.</summary>
    private static object SerializeStage(ChainStage s)
    {
        if (s.Gate is null && s.ElseBranch is null)
            return s.JobIds; // plain array — legacy compat

        return new
        {
            jobs = s.JobIds,
            gate = SerializeGate(s.Gate),
            elseBranch = s.ElseBranch?.Select(SerializeStage).ToList()
        };
    }

    private static object? SerializeGate(ChainGate? gate) => gate switch
    {
        null => null,
        NoGate => null,
        WaitGate w => new { type = "wait", durationSeconds = w.Duration.TotalSeconds },
        ConditionGate c => new
        {
            type = "condition",
            expression = c.Expression,
            elseBranch = c.ElseBranch?.Select(SerializeStage).ToList()
        },
        _ => null,
    };

    private static JobChain ToDomain(JobChainRow r) => JobChain.Rehydrate(
        r.Id, r.ProjectId, r.Name, r.Description, DeserializeStages(r.StagesJson), r.CreatedAt, r.UpdatedAt);

    /// <summary>
    /// Reads the stages JSON. Accepts:
    ///   - legacy flat array of job ids: <c>["id1","id2"]</c>
    ///   - array-of-arrays: <c>[["id1"],["id2"]]</c>
    ///   - array of objects with optional gate: <c>[{"jobs":["id1"],"gate":{...}}]</c>
    ///   - mixed: <c>[["id1"],{"jobs":["id2"],"gate":{...}}]</c>
    /// </summary>
    private static List<ChainStage> DeserializeStages(string stagesJson)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(stagesJson) ? "[]" : stagesJson);
        var root = doc.RootElement;
        var stages = new List<ChainStage>();
        foreach (var element in root.EnumerateArray())
        {
            stages.Add(DeserializeStageElement(element));
        }
        return stages;
    }

    private static ChainStage DeserializeStageElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var jobs = element.GetProperty("jobs")
                .EnumerateArray().Select(e => e.GetGuid());
            var gateObj = element.TryGetProperty("gate", out var g) ? DeserializeGateElement(g) : null;
            IReadOnlyList<ChainStage>? elseBranch = null;
            if (element.TryGetProperty("elseBranch", out var eb) && eb.ValueKind == JsonValueKind.Array)
            {
                elseBranch = eb.EnumerateArray().Select(DeserializeStageElement).ToList();
            }
            return new ChainStage(jobs, gateObj, elseBranch);
        }

        if (element.ValueKind == JsonValueKind.Array)
            return new ChainStage(element.EnumerateArray().Select(e => e.GetGuid()));

        // Legacy: a single guid
        return ChainStage.Of(element.GetGuid());
    }

    private static ChainGate? DeserializeGateElement(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty("type", out var typeProp)) return null;

        var type = typeProp.GetString();
        return type switch
        {
            "wait" when element.TryGetProperty("durationSeconds", out var dur) => new WaitGate(TimeSpan.FromSeconds(dur.GetDouble())),
            "condition" when element.TryGetProperty("expression", out var expr) => DeserializeConditionGate(expr.GetString(), element),
            _ => NoGate.Instance,
        };
    }

    private static ConditionGate DeserializeConditionGate(string? expression, JsonElement element)
    {
        IReadOnlyList<ChainStage>? elseBranch = null;
        if (element.TryGetProperty("elseBranch", out var eb) && eb.ValueKind == JsonValueKind.Array)
        {
            elseBranch = eb.EnumerateArray().Select(DeserializeStageElement).ToList();
        }
        return new ConditionGate(expression ?? "", elseBranch);
    }
}
