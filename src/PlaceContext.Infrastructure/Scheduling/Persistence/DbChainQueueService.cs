using System.Data;
using System.Text.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Jobs;
using PlaceContext.Application.Secrets;
using PlaceContext.Application.Tenancy;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Application.Access;

namespace PlaceContext.Infrastructure.Scheduling;

public sealed class DbChainQueueService(AppDbContext db, ICurrentTenant tenant, IPermissionService permissions,
    IJobChainSubmissionQueue submissions, IDataEncryptor encryptor) : IChainQueueService
{
    public async Task<IReadOnlyList<ChainQueueDefinition>> ListAsync(Guid projectId, CancellationToken ct = default)
    {
        await RequireAsync(Permission.ChainsManage, ct);
        await using var command = await CommandAsync("SELECT * FROM chain_queues WHERE \"TenantId\" = @tenant AND \"ProjectId\" = @project ORDER BY \"CreatedAt\" DESC", ct);
        command.Parameters.AddWithValue("project", projectId);
        var result = new List<ChainQueueDefinition>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(ReadQueue(reader));
        }
        return result;
    }

    public async Task<ChainQueueDefinition> SaveAsync(Guid? id, Guid projectId, Guid chainId, string name, bool enabled, int messageTtlSeconds = 86400, int receiptRetentionDays = 30, int maxAttempts = 3, int retryDelaySeconds = 3, int deliveryDelaySeconds = 0, CancellationToken ct = default)
    {
        await RequireAsync(Permission.ChainsManage, ct);
        if (messageTtlSeconds is < 1 or > 604800)
        {
            throw new ArgumentException("Message TTL must be between 1 and 604,800 seconds (7 days).");
        }
        if (receiptRetentionDays is < 1 or > 365)
        {
            throw new ArgumentException("Receipt retention must be between 1 and 365 days.");
        }
        if (maxAttempts is < 1 or > 10)
        {
            throw new ArgumentException("Maximum attempts must be between 1 and 10, including the first attempt.");
        }
        if (retryDelaySeconds is < 1 or > 86400)
        {
            throw new ArgumentException("Retry delay must be between 1 and 86,400 seconds.");
        }
        if (deliveryDelaySeconds is < 0 or > 604800)
        {
            throw new ArgumentException("Delivery delay must be between 0 and 604,800 seconds (7 days).");
        }
        name = name.Trim();
        if (name.Length is < 1 or > 120)
        {
            throw new ArgumentException("Queue name must contain 1–120 characters.");
        }
        if (!await db.JobChains.AsNoTracking().AnyAsync(chain => chain.Id == chainId && chain.ProjectId == projectId, ct))
        {
            throw new ArgumentException("Choose a chain in this project.");
        }
        await using var command = await CommandAsync(id.HasValue ? """
            UPDATE chain_queues SET "ChainId" = @chain, "Name" = @name, "Enabled" = @enabled, "MessageTtlSeconds" = @ttl, "ReceiptRetentionDays" = @retention, "MaxAttempts" = @maxAttempts, "RetryDelaySeconds" = @retryDelay, "DeliveryDelaySeconds" = @deliveryDelay, "UpdatedAt" = now()
            WHERE "Id" = @id AND "TenantId" = @tenant AND "ProjectId" = @project RETURNING *
            """ : """
            INSERT INTO chain_queues ("Id", "TenantId", "ProjectId", "ChainId", "Name", "Enabled", "CreatedAt", "UpdatedAt", "MessageTtlSeconds", "ReceiptRetentionDays", "MaxAttempts", "RetryDelaySeconds", "DeliveryDelaySeconds")
            VALUES (@id, @tenant, @project, @chain, @name, @enabled, now(), now(), @ttl, @retention, @maxAttempts, @retryDelay, @deliveryDelay) RETURNING *
            """, ct);
        command.Parameters.AddWithValue("id", id ?? Guid.NewGuid());
        command.Parameters.AddWithValue("project", projectId);
        command.Parameters.AddWithValue("chain", chainId);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("enabled", enabled);
        command.Parameters.AddWithValue("ttl", messageTtlSeconds);
        command.Parameters.AddWithValue("retention", receiptRetentionDays);
        command.Parameters.AddWithValue("maxAttempts", maxAttempts);
        command.Parameters.AddWithValue("retryDelay", retryDelaySeconds);
        command.Parameters.AddWithValue("deliveryDelay", deliveryDelaySeconds);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            throw new KeyNotFoundException("Queue not found.");
        }
        return ReadQueue(reader);
    }

    public async Task RetryAsync(Guid queueId, Guid trackingId, CancellationToken ct = default)
    {
        await RequireAsync(Permission.ChainsManage, ct);
        await RequireAsync(Permission.JobsRun, ct);
        await using var command = await CommandAsync("""
            UPDATE rpc_chain_submissions SET "Status" = 'Queued', "Attempts" = 0,
                "PreviousRunIds" = CASE WHEN "ChainRunId" = ANY("PreviousRunIds") THEN "PreviousRunIds"
                    ELSE array_append("PreviousRunIds", "ChainRunId") END,
                "ChainRunId" = @run, "NextAttemptAt" = now(),
                "ExpiresAt" = now() + make_interval(secs => "MessageTtlSeconds"),
                "StartedAt" = NULL, "FinishedAt" = NULL, "LastError" = NULL,
                "ClaimedBy" = NULL, "ClaimedAt" = NULL, "HeartbeatAt" = NULL
            WHERE "Id" = @id AND "TenantId" = @tenant AND "QueueId" = @queue
              AND "Status" = 'Failed' AND "InputPayload" IS NOT NULL
            """, ct);
        command.Parameters.AddWithValue("id", trackingId);
        command.Parameters.AddWithValue("queue", queueId);
        command.Parameters.AddWithValue("run", Guid.NewGuid());
        if (await command.ExecuteNonQueryAsync(ct) == 0)
        {
            throw new InvalidOperationException("Only failed messages with retained payloads can be retried. Refresh the queue.");
        }
    }

    public async Task DeleteAsync(Guid queueId, CancellationToken ct = default)
    {
        await RequireAsync(Permission.ChainsManage, ct);
        await using var command = await CommandAsync("DELETE FROM chain_queues WHERE \"Id\" = @id AND \"TenantId\" = @tenant", ct);
        command.Parameters.AddWithValue("id", queueId);
        if (await command.ExecuteNonQueryAsync(ct) == 0)
        {
            throw new KeyNotFoundException("Queue not found.");
        }
    }

    public async Task<JobChainSubmission> SubmitAsync(Guid queueId, JsonElement payload, string? idempotencyKey, DateTimeOffset? executeAt = null, CancellationToken ct = default)
    {
        await RequireAsync(Permission.JobsRun, ct);
        return await SubmitAuthorizedAsync(queueId, payload, idempotencyKey, executeAt, ct);
    }

    internal async Task<JobChainSubmission> SubmitAuthorizedAsync(Guid queueId, JsonElement payload, string? idempotencyKey, DateTimeOffset? executeAt, CancellationToken ct)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Send a JSON object as the request body.");
        }
        var json = payload.GetRawText();
        if (Encoding.UTF8.GetByteCount(json) > 1_048_576)
        {
            throw new ArgumentException("The JSON payload must be at most 1 MiB.");
        }
        idempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (idempotencyKey?.Length > 128)
        {
            throw new ArgumentException("Idempotency-Key must be at most 128 characters.");
        }
        var queue = await GetQueueAsync(queueId, ct) ?? throw new KeyNotFoundException("Queue not found.");
        if (!queue.Enabled)
        {
            throw new InvalidOperationException("Queue intake is paused.");
        }
        if (!await db.JobChains.AsNoTracking().AnyAsync(chain => chain.Id == queue.ChainId && chain.ProjectId == queue.ProjectId, ct))
        {
            throw new InvalidOperationException("The linked chain no longer exists. Update the queue before submitting.");
        }
        var now = DateTimeOffset.UtcNow;
        if (executeAt is { } scheduled && (scheduled <= now || scheduled > now.AddDays(365)))
        {
            throw new ArgumentException("X-Execute-At must be a future timestamp within 365 days.");
        }
        var receipt = await submissions.EnqueueAsync(queue.ProjectId, queue.ChainId, json,
                    idempotencyKey is null ? null : $"queue:{queue.Id:N}:{idempotencyKey}", ct, queue.Id, queue.MessageTtlSeconds, queue.ReceiptRetentionDays, queue.MaxAttempts, queue.RetryDelaySeconds,
                    executeAt ?? now.AddSeconds(queue.DeliveryDelaySeconds));
        return (await ReadMessagesAsync(queueId, receipt.TrackingId, ct)).SingleOrDefault() ?? receipt;
    }

    public async Task<IReadOnlyList<JobChainSubmission>> ListMessagesAsync(Guid queueId, CancellationToken ct = default)
    {
        await RequireAsync(Permission.ChainsManage, ct);
        return await ReadMessagesAsync(queueId, null, ct);
    }

    public async Task<JobChainSubmission?> GetMessageAsync(Guid queueId, Guid trackingId, CancellationToken ct = default)
    {
        await RequireAsync(Permission.JobsView, ct);
        return await GetAuthorizedAsync(queueId, trackingId, ct);
    }

    internal async Task<JobChainSubmission?> GetAuthorizedAsync(Guid queueId, Guid trackingId, CancellationToken ct) =>
        (await ReadMessagesAsync(queueId, trackingId, ct)).SingleOrDefault();

    private async Task<IReadOnlyList<JobChainSubmission>> ReadMessagesAsync(Guid queueId, Guid? trackingId, CancellationToken ct)
    {
        await using var command = await CommandAsync("""
            SELECT s."Id", s."ProjectId", s."ChainId", s."ChainRunId", CASE WHEN s."Status" = 'RetryPending' OR (r."Status" = 'Failed' AND s."Status" IN ('Running', 'Waiting', 'AwaitingInput'))
                       THEN 'RetryPending' ELSE COALESCE(r."Status", s."Status") END,
                   s."Attempts", s."LastError", s."SubmittedAt", s."StartedAt", COALESCE(r."FinishedAt", s."FinishedAt"), s."PreviousRunIds", s."NextAttemptAt", s."ExpiresAt"
            FROM rpc_chain_submissions s LEFT JOIN chain_runs r ON r."Id" = s."ChainRunId" AND r."TenantId" = s."TenantId"
            WHERE s."TenantId" = @tenant AND s."QueueId" = @queue AND (@tracking IS NULL OR s."Id" = @tracking)
            ORDER BY s."SubmittedAt" DESC LIMIT 50
            """, ct);
        command.Parameters.AddWithValue("queue", queueId);
        command.Parameters.AddWithValue("tracking", NpgsqlTypes.NpgsqlDbType.Uuid, (object?)trackingId ?? DBNull.Value);
        var result = new List<JobChainSubmission>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new JobChainSubmission(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3),
            reader.GetString(4), reader.GetInt32(5), reader.IsDBNull(6) ? null : encryptor.Unprotect(reader.GetString(6), IDataEncryptor.Purpose.RpcChainSubmission),
            reader.GetFieldValue<DateTimeOffset>(7), reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
            reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9))
            {
                PreviousRunIds = reader.GetFieldValue<Guid[]>(10),
                AvailableAt = reader.GetFieldValue<DateTimeOffset>(11),
                ExpiresAt = reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12)
            });
        }
        return result;
    }

    internal async Task<ChainQueueDefinition?> GetQueueAsync(Guid id, CancellationToken ct)
    {
        await using var command = await CommandAsync("SELECT * FROM chain_queues WHERE \"Id\" = @id AND \"TenantId\" = @tenant", ct);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadQueue(reader) : null;
    }

    private async Task RequireAsync(string permission, CancellationToken ct)
    {
        if (!tenant.IsResolved || !await permissions.HasAsync(permission, ct))
        {
            throw new UnauthorizedAccessException("Permission denied.");
        }
    }

    private async Task<NpgsqlCommand> CommandAsync(string sql, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }
        var command = connection.CreateCommand();
        command.Transaction = (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = sql;
        command.Parameters.AddWithValue("tenant", tenant.TenantId);
        return command;
    }

    private static ChainQueueDefinition ReadQueue(NpgsqlDataReader reader) => new(reader.GetGuid(reader.GetOrdinal("Id")),
        reader.GetGuid(reader.GetOrdinal("ProjectId")), reader.GetGuid(reader.GetOrdinal("ChainId")), reader.GetString(reader.GetOrdinal("Name")),
        reader.GetBoolean(reader.GetOrdinal("Enabled")), reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("CreatedAt")),
        reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("UpdatedAt")), reader.GetInt32(reader.GetOrdinal("MessageTtlSeconds")),
        reader.GetInt32(reader.GetOrdinal("ReceiptRetentionDays")), reader.GetInt32(reader.GetOrdinal("MaxAttempts")),
        reader.GetInt32(reader.GetOrdinal("RetryDelaySeconds")), reader.GetInt32(reader.GetOrdinal("DeliveryDelaySeconds")));
}
