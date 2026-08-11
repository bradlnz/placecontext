using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfAgentDefinitionRepository(AppDbContext db) : IAgentDefinitionRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AgentDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var row = await db.AgentDefinitions.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<AgentDefinition?> GetCommandAsync(Guid projectId, CancellationToken ct = default)
    {
        var row = await db.AgentDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(item => item.ProjectId == projectId && item.Kind == nameof(AgentKind.Command), ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<AgentDefinition>> ListForProjectAsync(Guid projectId, CancellationToken ct = default)
        => (await db.AgentDefinitions.AsNoTracking()
            .Where(item => item.ProjectId == projectId)
            .ToListAsync(ct)).Select(ToDomain).ToArray();

    public Task AddAsync(AgentDefinition agent, CancellationToken ct = default)
        => db.AgentDefinitions.AddAsync(ToRow(agent), ct).AsTask();

    public async Task UpdateAsync(AgentDefinition agent, CancellationToken ct = default)
    {
        var row = await db.AgentDefinitions.FindAsync([agent.Id], ct);
        if (row is null)
            throw new InvalidOperationException($"Agent {agent.Id} not found.");
        Copy(agent, row);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        var row = await db.AgentDefinitions.FindAsync([id], ct);
        if (row is not null)
            db.AgentDefinitions.Remove(row);
    }

    private static AgentDefinition ToDomain(AgentDefinitionRow row)
        => AgentDefinition.Rehydrate(
            row.Id, row.ProjectId, Enum.Parse<AgentKind>(row.Kind), row.Name, row.Description,
            row.Instructions, row.TemplateKey,
            Deserialize<AgentCapability>(row.CapabilitiesJson), Deserialize<Guid>(row.AllowedJobIdsJson),
            row.Enabled, row.ParentAgentId, row.CreatedAt, row.UpdatedAt);

    private static AgentDefinitionRow ToRow(AgentDefinition agent)
    {
        var row = new AgentDefinitionRow { Id = agent.Id, ProjectId = agent.ProjectId };
        Copy(agent, row);
        row.CreatedAt = agent.CreatedAt;
        return row;
    }

    private static void Copy(AgentDefinition agent, AgentDefinitionRow row)
    {
        row.Kind = agent.Kind.ToString();
        row.Name = agent.Name;
        row.Description = agent.Description;
        row.Instructions = agent.Instructions;
        row.TemplateKey = agent.TemplateKey;
        row.ParentAgentId = agent.ParentAgentId;
        row.CapabilitiesJson = JsonSerializer.Serialize(agent.Capabilities, Json);
        row.AllowedJobIdsJson = JsonSerializer.Serialize(agent.AllowedJobIds, Json);
        row.Enabled = agent.Enabled;
        row.UpdatedAt = agent.UpdatedAt;
    }

    private static IReadOnlyList<T> Deserialize<T>(string json)
    {
        try { return JsonSerializer.Deserialize<T[]>(json, Json) ?? []; }
        catch (JsonException) { return []; }
    }
}
