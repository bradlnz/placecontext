using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Networking;

public sealed class DbWorkerNetworkService(AppDbContext db, ICurrentTenant tenant, IPermissionService permissions) : IWorkerNetworkService
{
    public async Task<WorkerNetworkState> LoadAsync(Guid projectId, CancellationToken ct = default)
    {
        if (!tenant.IsResolved || (!await permissions.HasAsync(Permission.SettingsManage, ct) && !await permissions.HasAsync(Permission.BackupManage, ct)))
            throw new UnauthorizedAccessException("Network administration or backup permission is required.");
        await using var command = await CommandAsync("""
            SELECT "Id", "Name", "Cidr", "Groups", "Version" FROM worker_networks
            WHERE "TenantId" = @tenant AND "ProjectId" = @project ORDER BY "Name";
            SELECT "JobId", "NetworkId", "GroupId" FROM job_network_assignments
            WHERE "TenantId" = @tenant AND "ProjectId" = @project;
            """, ct);
        command.Parameters.AddWithValue("project", projectId);
        var networks = new List<WorkerNetwork>();
        var assignments = new List<JobNetworkAssignment>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) networks.Add(new(reader.GetGuid(0), projectId, reader.GetString(1), reader.GetString(2),
            JsonSerializer.Deserialize<NetworkSecurityGroup[]>(reader.GetString(3))!, reader.GetInt64(4)));
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct)) assignments.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2)));
        return new(networks, assignments);
    }

    public async Task SaveAsync(WorkerNetwork network, CancellationToken ct = default)
    {
        await RequireAsync(ct);
        WorkerNetworkPolicy.ValidateNetwork(network.Name, network.Cidr, network.Groups);
        if (network.Id == Guid.Empty || !await db.Projects.AnyAsync(project => project.Id == network.ProjectId, ct))
            throw new ArgumentException("Choose a project in this workspace.");
        // VPC identity is immutable; group IDs stay stable for existing assignments.
        await using var command = await CommandAsync(network.Version == 0 ? """
            INSERT INTO worker_networks ("Id", "TenantId", "ProjectId", "Name", "Cidr", "Groups", "Version")
            VALUES (@id, @tenant, @project, @name, @cidr, @groups, 1)
            """ : """
            UPDATE worker_networks SET "Groups" = @groups, "Version" = "Version" + 1
            WHERE "Id" = @id AND "TenantId" = @tenant AND "ProjectId" = @project
              AND "Name" = @name AND "Cidr" = @cidr AND "Version" = @version
              AND NOT EXISTS (SELECT 1 FROM jsonb_array_elements("Groups") old_group
                WHERE NOT EXISTS (SELECT 1 FROM jsonb_array_elements(@groups) new_group
                  WHERE new_group->>'Id' = old_group->>'Id'))
            """, ct);
        command.Parameters.AddWithValue("id", network.Id);
        command.Parameters.AddWithValue("project", network.ProjectId);
        command.Parameters.AddWithValue("name", network.Name.Trim());
        command.Parameters.AddWithValue("cidr", network.Cidr);
        command.Parameters.AddWithValue("groups", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(network.Groups));
        command.Parameters.AddWithValue("version", network.Version);
        if (await command.ExecuteNonQueryAsync(ct) != 1)
            throw new InvalidOperationException("Reload before saving. VPC names and CIDRs are locked after creation; existing security groups cannot be removed.");
    }

    public async Task DeleteAsync(Guid projectId, Guid networkId, long version, CancellationToken ct = default)
    {
        await RequireAsync(ct);
        await using var command = await CommandAsync("""
            SELECT "Version" FROM worker_networks
            WHERE "TenantId" = @tenant AND "ProjectId" = @project AND "Id" = @id FOR UPDATE
            """, ct);
        await using var transaction = await command.Connection!.BeginTransactionAsync(ct);
        command.Transaction = transaction;
        command.Parameters.AddWithValue("project", projectId);
        command.Parameters.AddWithValue("id", networkId);
        if (await command.ExecuteScalarAsync(ct) is not long current || current != version)
            throw new InvalidOperationException("The VPC changed or no longer exists. Refresh before deleting.");
        // The parent lock prevents a concurrent assignment from being deleted by the cascade.
        command.CommandText = """
            DELETE FROM worker_networks WHERE "TenantId" = @tenant AND "ProjectId" = @project AND "Id" = @id
              AND NOT EXISTS (SELECT 1 FROM job_network_assignments WHERE "NetworkId" = @id AND "TenantId" = @tenant)
            """;
        if (await command.ExecuteNonQueryAsync(ct) != 1)
            throw new InvalidOperationException("Remove job assignments before deleting this VPC.");
        await transaction.CommitAsync(ct);
        // shortcut: retain provisioned cluster resources for in-flight runs; add reconciled cleanup before automatic reclamation.
    }

    public async Task AssignAsync(Guid projectId, Guid jobId, Guid? networkId, Guid? groupId, CancellationToken ct = default)
    {
        await RequireAsync(ct);
        if (!await db.Jobs.AnyAsync(job => job.Id == jobId && job.ProjectId == projectId, ct))
            throw new ArgumentException("Choose a job in this project.");
        if (networkId is null)
        {
            await using var remove = await CommandAsync("DELETE FROM job_network_assignments WHERE \"TenantId\" = @tenant AND \"JobId\" = @job", ct);
            remove.Parameters.AddWithValue("job", jobId);
            await remove.ExecuteNonQueryAsync(ct);
            return;
        }
        await using var command = await CommandAsync("""
            INSERT INTO job_network_assignments ("TenantId", "ProjectId", "JobId", "NetworkId", "GroupId")
            SELECT @tenant, @project, @job, "Id", @group FROM worker_networks
            WHERE "TenantId" = @tenant AND "ProjectId" = @project AND "Id" = @network
              AND EXISTS (SELECT 1 FROM jsonb_array_elements("Groups") g WHERE g->>'Id' = @group::text)
            ON CONFLICT ("TenantId", "JobId") DO UPDATE SET "NetworkId" = EXCLUDED."NetworkId", "GroupId" = EXCLUDED."GroupId"
            """, ct);
        command.Parameters.AddWithValue("project", projectId);
        command.Parameters.AddWithValue("job", jobId);
        command.Parameters.AddWithValue("network", networkId.Value);
        command.Parameters.AddWithValue("group", groupId ?? Guid.Empty);
        if (await command.ExecuteNonQueryAsync(ct) != 1) throw new ArgumentException("Choose a VPC and security group in this project.");
    }

    public async Task<WorkerNetworkPolicy?> ResolveAsync(Guid projectId, Guid jobId, CancellationToken ct = default)
    {
        if (!tenant.IsResolved) throw new UnauthorizedAccessException("Workspace is required.");
        await using var command = await CommandAsync("""
            SELECT n."Id", n."Cidr", n."Groups", a."GroupId" FROM job_network_assignments a
            JOIN worker_networks n ON n."Id" = a."NetworkId" AND n."TenantId" = a."TenantId" AND n."ProjectId" = a."ProjectId"
            WHERE a."TenantId" = @tenant AND a."ProjectId" = @project AND a."JobId" = @job
            """, ct);
        command.Parameters.AddWithValue("project", projectId);
        command.Parameters.AddWithValue("job", jobId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var group = JsonSerializer.Deserialize<NetworkSecurityGroup[]>(reader.GetString(2))!.Single(group => group.Id == reader.GetGuid(3));
        return new(tenant.TenantId, reader.GetGuid(0), reader.GetString(1), group);
    }

    private async Task RequireAsync(CancellationToken ct)
    {
        if (!tenant.IsResolved || !await permissions.HasAsync(Permission.SettingsManage, ct)) throw new UnauthorizedAccessException("Network administration permission is required.");
    }

    private async Task<NpgsqlCommand> CommandAsync(string sql, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);
        var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant", tenant.TenantId);
        return command;
    }
}
