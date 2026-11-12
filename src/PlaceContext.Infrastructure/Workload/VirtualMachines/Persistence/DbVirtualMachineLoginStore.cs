using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Secrets;
using PlaceContext.Application.Tenancy;
using PlaceContext.Application.Workload.VirtualMachines;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Application.Access;

namespace PlaceContext.Infrastructure.Workload;

public sealed class DbVirtualMachineLoginStore(DbContextOptions<AppDbContext> options, ICurrentTenant tenant,
    ICurrentUser user, IPermissionService permissions, IDataEncryptor encryptor) : IVirtualMachineLoginStore
{
    internal const string Schema = """
        CREATE TABLE IF NOT EXISTS qemu_login_profiles (
          "TenantId" uuid NOT NULL, "ProjectId" uuid NOT NULL, "UserId" uuid NOT NULL, "Id" uuid NOT NULL,
          "Name" varchar(80) NOT NULL, "ImageProfile" varchar(80) NOT NULL,
          "UpdatedAt" timestamptz NOT NULL, "Profile" bytea NOT NULL,
          PRIMARY KEY ("TenantId", "ProjectId", "UserId", "Id"));
        """;

    private string Purpose(Guid projectId) => $"qemu.login.v1.{tenant.TenantId}.{projectId}.{user.UserId}";
    private async Task<AppDbContext> OpenAsync(Guid projectId, CancellationToken ct)
    {
        if (!tenant.IsResolved || !user.IsAuthenticated || !await permissions.HasAsync(Permission.JobsRun, ct))
        {
            throw new UnauthorizedAccessException("Workflow execution permission is required.");
        }
        var db = new AppDbContext(options, tenant, user);
        try
        {
            if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct))
            {
                throw new UnauthorizedAccessException("Project unavailable.");
            }
            await db.Database.OpenConnectionAsync(ct);
            return db;
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }
    private NpgsqlCommand Command(AppDbContext db, Guid projectId, string sql)
    {
        var command = new NpgsqlCommand(sql, (NpgsqlConnection)db.Database.GetDbConnection());
        command.Parameters.AddWithValue("tenant", tenant.TenantId);
        command.Parameters.AddWithValue("project", projectId);
        command.Parameters.AddWithValue("user", user.UserId);
        return command;
    }
    private const string Scope = "\"TenantId\"=@tenant AND \"ProjectId\"=@project AND \"UserId\"=@user";
    public async Task<IReadOnlyList<VirtualMachineLoginProfile>> ListAsync(Guid projectId, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(projectId, ct);
        await using var command = Command(db, projectId, $"SELECT \"Id\",\"Name\",\"ImageProfile\",\"UpdatedAt\" FROM qemu_login_profiles WHERE {Scope} ORDER BY \"UpdatedAt\" DESC LIMIT 20");
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<VirtualMachineLoginProfile>();
        while (await reader.ReadAsync(ct))
        {
            result.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3)));
        }
        return result;
    }
    public async Task<byte[]> ReadAsync(Guid projectId, Guid profileId, string imageProfile, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(projectId, ct);
        await using var command = Command(db, projectId, $"SELECT \"Profile\" FROM qemu_login_profiles WHERE {Scope} AND \"Id\"=@id AND \"ImageProfile\"=@image");
        command.Parameters.AddWithValue("id", profileId);
        command.Parameters.AddWithValue("image", imageProfile);
        if (await command.ExecuteScalarAsync(ct) is not byte[] data)
        {
            throw new UnauthorizedAccessException("Saved login unavailable. Choose one of your own sessions for this image profile.");
        }
        var profile = encryptor.UnprotectBytes(data, Purpose(projectId));
        VirtualMachineProfileArchive.Validate(profile);
        return profile;
    }
    public async Task SaveAsync(Guid projectId, string name, string imageProfile, byte[] profile, CancellationToken ct = default)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl))
        {
            throw new ArgumentException("Name the saved login using 1–80 characters.");
        }
        VirtualMachineProfileArchive.Validate(profile);
        await using var db = await OpenAsync(projectId, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Serialize the small per-owner quota check without locking other projects or users.
        await using var command = Command(db, projectId, "SELECT pg_advisory_xact_lock(hashtextextended(@lock,0))");
        command.Parameters.AddWithValue("lock", Purpose(projectId));
        command.Transaction = (NpgsqlTransaction)transaction.GetDbTransaction();
        await command.ExecuteNonQueryAsync(ct);
        command.CommandText = $"SELECT count(*) FROM qemu_login_profiles WHERE {Scope}";
        if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) >= 20)
        {
            throw new InvalidOperationException("Delete an unused login before saving another (20 per project).");
        }
        command.CommandText = "INSERT INTO qemu_login_profiles VALUES (@tenant,@project,@user,@id,@name,@image,@updated,@profile)";
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("image", imageProfile);
        command.Parameters.AddWithValue("updated", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("profile", encryptor.ProtectBytes(profile, Purpose(projectId)));
        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }
    public async Task DeleteAsync(Guid projectId, Guid profileId, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(projectId, ct);
        await using var command = Command(db, projectId, $"DELETE FROM qemu_login_profiles WHERE {Scope} AND \"Id\"=@id");
        command.Parameters.AddWithValue("id", profileId);
        await command.ExecuteNonQueryAsync(ct);
    }
}
