using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using PlaceContext.Application.Billing;
using PlaceContext.Application.Jobs;
using PlaceContext.Application.Secrets;
using PlaceContext.Application.Tenancy;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Scheduling;

/// <summary>Encrypted, tenant-isolated and idempotent durable intake for managed API job runs.
/// The caller must be authorized by the Core API JobsRun policy before invoking this service.</summary>
public sealed class DbJobRunSubmissionQueue(
    AppDbContext db, ICurrentTenant tenant, IDataEncryptor encryptor,
    IOptions<RunBillingDeploymentOptions>? hosting = null, IRunAllowance? allowance = null) : IJobRunSubmissionQueue
{
    private static string Purpose => IDataEncryptor.Purpose.PendingRun;

    public async Task<JobRunSubmission> EnqueueAsync(Guid projectId, Guid jobId, string? inputPayload,
        Guid? runId = null, CancellationToken ct = default)
    {
        if (!tenant.IsResolved || tenant.TenantId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("A resolved tenant is required.");
        }
        if (projectId == Guid.Empty || jobId == Guid.Empty || runId == Guid.Empty)
        {
            throw new ArgumentException("A nonempty project, job and run id are required.");
        }
        if (hosting?.Value.ManagedHosting == true
                    && (allowance is null || !(await allowance.GetAsync(ct)).CanRun))
        {
            throw new InvalidOperationException("A managed subscription must be active before queuing jobs.");
        }
        var id = runId ?? Guid.NewGuid();
        var trackingId = Guid.NewGuid();
        var protectedPayload = inputPayload is null ? null : encryptor.Protect(inputPayload, Purpose);
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using (var validate = conn.CreateCommand())
        {
            validate.Transaction = tx;
            validate.CommandText = """
                SELECT EXISTS(SELECT 1 FROM jobs
                WHERE "TenantId" = @tenant AND "ProjectId" = @project AND "Id" = @job)
                """;
            validate.Parameters.AddWithValue("tenant", tenant.TenantId);
            validate.Parameters.AddWithValue("project", projectId);
            validate.Parameters.AddWithValue("job", jobId);
            if (await validate.ExecuteScalarAsync(ct) is not true)
            {
                throw new ArgumentException("Job not found in this project.", nameof(jobId));
            }
        }
        // Serialize retries with the same id across replicas before reading an existing
        // receipt. An application-selected id cannot spend a new run slot twice.
        await using (var lockCommand = conn.CreateCommand())
        {
            lockCommand.Transaction = tx;
            lockCommand.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@scope, 0))";
            lockCommand.Parameters.AddWithValue("scope", $"{tenant.TenantId:N}:job:{id:N}");
            await lockCommand.ExecuteNonQueryAsync(ct);
        }
        await using (var insert = conn.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO rpc_job_submissions
                ("Id", "TenantId", "ProjectId", "JobId", "RunId", "InputPayload",
                 "Status", "Attempts", "SubmittedAt", "NextAttemptAt")
                VALUES (@id, @tenant, @project, @job, @run, @payload,
                        'Queued', 0, now(), now())
                ON CONFLICT ("TenantId", "RunId") DO NOTHING
                """;
            insert.Parameters.AddWithValue("id", trackingId);
            insert.Parameters.AddWithValue("tenant", tenant.TenantId);
            insert.Parameters.AddWithValue("project", projectId);
            insert.Parameters.AddWithValue("job", jobId);
            insert.Parameters.AddWithValue("run", id);
            insert.Parameters.AddWithValue("payload", (object?)protectedPayload ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync(ct);
        }
        JobRunSubmission result;
        await using (var read = conn.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = """
                SELECT "Id", "ProjectId", "JobId", "RunId", "Status", "Attempts",
                       "LastError", "SubmittedAt", "StartedAt", "FinishedAt", "InputPayload"
                FROM rpc_job_submissions
                WHERE "TenantId" = @tenant AND "RunId" = @run
                """;
            read.Parameters.AddWithValue("tenant", tenant.TenantId);
            read.Parameters.AddWithValue("run", id);
            await using var reader = await read.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                throw new InvalidOperationException("Job queue receipt missing.");
            }
            result = Read(reader);
            if (result.JobId != jobId || result.ProjectId != projectId)
            {
                throw new InvalidOperationException("The run id was previously used by another job.");
            }
            var stored = reader.IsDBNull(10) ? null : encryptor.Unprotect(reader.GetString(10), Purpose);
            if (!string.Equals(inputPayload, stored, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The run id was previously submitted with different inputs.");
            }
        }
        await tx.CommitAsync(ct);
        return result;
    }

    public async Task<JobRunSubmission?> GetAsync(Guid projectId, Guid jobId, Guid trackingId,
        CancellationToken ct = default)
    {
        if (!tenant.IsResolved || tenant.TenantId == Guid.Empty || trackingId == Guid.Empty)
        {
            return null;
        }
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }
        await using var command = conn.CreateCommand();
        command.CommandText = """
            SELECT s."Id", s."ProjectId", s."JobId", s."RunId",
                   CASE WHEN s."Status" IN ('Succeeded','Partial','Failed','Cancelled')
                        THEN s."Status" ELSE COALESCE(j."Status", s."Status") END,
                   s."Attempts", s."LastError", s."SubmittedAt", s."StartedAt", s."FinishedAt"
            FROM rpc_job_submissions s
            LEFT JOIN job_runs j ON j."Id" = s."RunId" AND j."TenantId" = s."TenantId"
            WHERE s."Id" = @id AND s."TenantId" = @tenant
              AND s."ProjectId" = @project AND s."JobId" = @job
            """;
        command.Parameters.AddWithValue("id", trackingId);
        command.Parameters.AddWithValue("tenant", tenant.TenantId);
        command.Parameters.AddWithValue("project", projectId);
        command.Parameters.AddWithValue("job", jobId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    private JobRunSubmission Read(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3),
        reader.GetString(4), reader.GetInt32(5),
        reader.IsDBNull(6) ? null : encryptor.Unprotect(reader.GetString(6), Purpose),
        reader.GetFieldValue<DateTimeOffset>(7),
        reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
        reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9));
}
