using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Access;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Build;
using PlaceContext.Application.Secrets;
using PlaceContext.Application.Tenancy;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Build;

public sealed class DbProjectAgentStore(IServiceScopeFactory scopes, ICurrentUser user, ICurrentTenant tenant,
    IPermissionService permissions, IDataEncryptor encryption, IModelConnections models) : IProjectAgentStore
{
    private const string Purpose = "project.agents.v1";

    private async Task CheckAsync(string permission, CancellationToken ct)
    {
        if (!user.IsAuthenticated || !tenant.IsResolved || !await permissions.HasAsync(Permission.ProjectsView, ct)
            || !await permissions.HasAsync(permission, ct))
            throw new UnauthorizedAccessException("Project agent access is required.");
    }

    public async Task<ProjectAgentCatalog> GetAsync(Guid projectId, CancellationToken ct = default)
    {
        await CheckAsync(Permission.ProjectsView, ct);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new InvalidOperationException("Project not found.");
        var catalog = row.AgentSettings is null ? new() : JsonSerializer.Deserialize<ProjectAgentCatalog>(encryption.Unprotect(row.AgentSettings, Purpose))!;
        catalog.Version = row.AgentSettingsVersion;
        return catalog;
    }

    public async Task SaveAsync(Guid projectId, ProjectAgentCatalog catalog, CancellationToken ct = default)
    {
        await CheckAsync(Permission.ProjectsManage, ct);
        catalog.Validate();
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new InvalidOperationException("Project not found.");
        if (row.AgentSettingsVersion != catalog.Version)
            throw new InvalidOperationException("Agents changed in another session. Close the editor and refresh before saving again.");
        var previous = row.AgentSettings is null ? new ProjectAgentCatalog()
            : JsonSerializer.Deserialize<ProjectAgentCatalog>(encryption.Unprotect(row.AgentSettings, Purpose))!;
        // Deleted links must not block unrelated edits; validate only newly linked workflows.
        var workflows = catalog.Agents.SelectMany(a => a.WorkflowIds.Except(previous.Agents.FirstOrDefault(p => p.Id == a.Id)?.WorkflowIds ?? [])).Distinct().ToArray();
        if (workflows.Length > 0 && await db.JobChains.CountAsync(w => w.ProjectId == projectId && workflows.Contains(w.Id), ct) != workflows.Length)
            throw new ArgumentException("Link only available workflows from this project.");
        var assigned = catalog.Agents.SelectMany(a => a.McpConnectionIds).Distinct().ToArray();
        if (await db.McpConnections.CountAsync(c => c.ProjectId == projectId && c.Enabled && assigned.Contains(c.Id), ct) != assigned.Length)
            throw new ArgumentException("Assign only enabled MCP connections from this project. Remove unavailable connections before saving.");
        var connections = await models.ListAsync(ct);
        if (catalog.Agents.Any(a => a.ConnectionId != Guid.Empty && !connections.Any(c => c.Id == a.ConnectionId && c.Enabled && c.Models.Contains(a.Model))))
            throw new ArgumentException("Select an available model or use the runner's default model.");
        row.AgentSettings = encryption.Protect(JsonSerializer.Serialize(catalog), Purpose);
        row.AgentSettingsVersion++;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("Agents changed in another session. Close the editor and refresh before saving again.");
        }
        catalog.Version++;
    }
}
