using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Scheduling;

/// <summary>
/// PostgreSQL-backed MCP chain submission queue. Payloads and errors are encrypted before storage;
/// receipt reads are tenant-scoped even though the worker drains the table globally.
/// </summary>
public sealed class DbJobChainSubmissionQueue : IJobChainSubmissionQueue
{
    private const int MaxIdempotencyKeyLength = 200;
    private readonly AppDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly ICurrentUser _user;
    private readonly IDataEncryptor _encryptor;

    public DbJobChainSubmissionQueue(
        AppDbContext db,
        ICurrentTenant tenant,
        ICurrentUser user,
        IDataEncryptor encryptor)
        => (_db, _tenant, _user, _encryptor) = (db, tenant, user, encryptor);

    public async Task<JobChainSubmission> EnqueueAsync(
        Guid projectId,
        Guid chainId,
        string? inputPayload,
        string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        if (!_tenant.IsResolved)
            throw new InvalidOperationException("A tenant is required to submit a job chain.");
        if (!_user.IsAuthenticated)
            throw new InvalidOperationException("An authenticated user is required to submit a job chain.");
        if (projectId == Guid.Empty) throw new ArgumentException("Project id is required.", nameof(projectId));
        if (chainId == Guid.Empty) throw new ArgumentException("Chain id is required.", nameof(chainId));

        idempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (idempotencyKey?.Length > MaxIdempotencyKeyLength)
            throw new ArgumentException(
                $"Idempotency key must not exceed {MaxIdempotencyKeyLength} characters.",
                nameof(idempotencyKey));

        var trackingId = Guid.NewGuid();
        var chainRunId = Guid.NewGuid();
        var protectedPayload = inputPayload is null
            ? null
            : _encryptor.Protect(inputPayload, IDataEncryptor.Purpose.RpcChainSubmission);

        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        if (idempotencyKey is not null)
        {
            // PostgreSQL can observe a concurrent ON CONFLICT row without making it visible to a
            // SELECT in the same statement snapshot. Serialize only equal tenant/key pairs so every
            // caller reliably receives the winning receipt instead of an empty result race.
            await using var idempotencyLock = connection.CreateCommand();
            idempotencyLock.Transaction = transaction;
            idempotencyLock.CommandText =
                "SELECT pg_advisory_xact_lock(hashtextextended(@scope, 0))";
            idempotencyLock.Parameters.AddWithValue(
                "scope", $"{_tenant.TenantId:N}:{idempotencyKey}");
            await idempotencyLock.ExecuteNonQueryAsync(ct);
        }
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            WITH inserted AS (
                INSERT INTO rpc_chain_submissions
                    ("Id", "TenantId", "ProjectId", "ChainId", "ChainRunId", "IdempotencyKey",
                     "InputPayload", "SubmitterUserId", "SubmitterRole", "Status", "Attempts",
                     "SubmittedAt", "NextAttemptAt")
                VALUES
                    (@id, @tenant, @project, @chain, @run, @key,
                     @payload, @user, @role, 'Queued', 0, now(), now())
                ON CONFLICT ("TenantId", "IdempotencyKey")
                    WHERE "IdempotencyKey" IS NOT NULL
                DO NOTHING
                RETURNING "Id", "ProjectId", "ChainId", "ChainRunId", "Status", "Attempts",
                          "LastError", "SubmittedAt", "StartedAt", "FinishedAt"
            )
            SELECT * FROM inserted
            UNION ALL
            SELECT "Id", "ProjectId", "ChainId", "ChainRunId", "Status", "Attempts",
                   "LastError", "SubmittedAt", "StartedAt", "FinishedAt"
            FROM rpc_chain_submissions
            WHERE @key IS NOT NULL AND "TenantId" = @tenant AND "IdempotencyKey" = @key
              AND NOT EXISTS (SELECT 1 FROM inserted)
            LIMIT 1
            """;
        command.Parameters.AddWithValue("id", trackingId);
        command.Parameters.AddWithValue("tenant", _tenant.TenantId);
        command.Parameters.AddWithValue("project", projectId);
        command.Parameters.AddWithValue("chain", chainId);
        command.Parameters.AddWithValue("run", chainRunId);
        command.Parameters.AddWithValue("key", (object?)idempotencyKey ?? DBNull.Value);
        command.Parameters.AddWithValue("payload", (object?)protectedPayload ?? DBNull.Value);
        command.Parameters.AddWithValue("user", _user.UserId);
        command.Parameters.AddWithValue("role", _user.Role);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new InvalidOperationException("The chain submission could not be persisted.");
        var result = Read(reader);
        await reader.DisposeAsync();
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<JobChainSubmission?> GetAsync(Guid trackingId, CancellationToken ct = default)
    {
        if (!_tenant.IsResolved || trackingId == Guid.Empty) return null;

        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT "Id", "ProjectId", "ChainId", "ChainRunId", "Status", "Attempts",
                   "LastError", "SubmittedAt", "StartedAt", "FinishedAt"
            FROM rpc_chain_submissions
            WHERE "Id" = @id AND "TenantId" = @tenant
            """;
        command.Parameters.AddWithValue("id", trackingId);
        command.Parameters.AddWithValue("tenant", _tenant.TenantId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    private JobChainSubmission Read(NpgsqlDataReader reader)
    {
        var error = reader.IsDBNull(6)
            ? null
            : _encryptor.Unprotect(reader.GetString(6), IDataEncryptor.Purpose.RpcChainSubmission);
        return new JobChainSubmission(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetString(4),
            reader.GetInt32(5),
            error,
            reader.GetFieldValue<DateTimeOffset>(7),
            reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
            reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9));
    }
}
