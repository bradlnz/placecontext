using System.Data;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Billing;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Jobs;
using PlaceContext.Application.Secrets;
using PlaceContext.Application.Tenancy;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Application.Access;

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
    private readonly IPermissionService _permissions;
    private readonly IOptions<RunBillingDeploymentOptions>? _hosting;
    private readonly IRunAllowance? _allowance;

    public DbJobChainSubmissionQueue(
        AppDbContext db,
        ICurrentTenant tenant,
        ICurrentUser user,
        IDataEncryptor encryptor, IPermissionService permissions,
        IOptions<RunBillingDeploymentOptions>? hosting = null, IRunAllowance? allowance = null)
    {
        (_db, _tenant, _user, _encryptor, _permissions) = (db, tenant, user, encryptor, permissions);
        _hosting = hosting;
        _allowance = allowance;
    }

    public async Task<JobChainSubmission> EnqueueAsync(
        Guid projectId,
        Guid chainId,
        string? inputPayload,
        string? idempotencyKey = null,
        CancellationToken ct = default,
        Guid? queueId = null, int? messageTtlSeconds = null, int receiptRetentionDays = 30, int maxAttempts = 3, int retryDelaySeconds = 3, DateTimeOffset? availableAt = null,
        IReadOnlyDictionary<int, string>? stepPayloadOverrides = null,
        Guid? replayOriginalRunId = null, int? replayFromStepIndex = null)
    {
        if (!_tenant.IsResolved)
        {
            throw new InvalidOperationException("A tenant is required to submit a job chain.");
        }
        if (!_user.IsAuthenticated || !await _permissions.HasAsync(Permission.JobsRun, ct))
        {
            throw new UnauthorizedAccessException("Permission to start workflows is required.");
        }
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Project id is required.", nameof(projectId));
        }
        if (chainId == Guid.Empty)
        {
            throw new ArgumentException("Chain id is required.", nameof(chainId));
        }
        var usage = _hosting?.Value.ManagedHosting == true && _allowance is not null
            ? await _allowance.GetAsync(ct) : null;
        if (_hosting?.Value.ManagedHosting == true && usage?.SubscriptionActive != true)
        {
            throw new InvalidOperationException("A managed subscription must be active before queuing workflows.");
        }
        if (!await _db.JobChains.AsNoTracking().AnyAsync(c => c.Id == chainId && c.ProjectId == projectId, ct))
        {
            throw new ArgumentException("Workflow does not belong to this project.", nameof(chainId));
        }
        if (replayOriginalRunId == Guid.Empty
                    || (replayOriginalRunId is null && replayFromStepIndex is not null)
                    || replayFromStepIndex is < 0)
        {
            throw new ArgumentException("Replay metadata is invalid.");
        }
        if (replayOriginalRunId is { } originalId
                    && !await _db.ChainRuns.AsNoTracking().AnyAsync(r =>
                        r.Id == originalId && r.ChainId == chainId, ct))
        {
            throw new ArgumentException("Original run does not belong to this workflow.");
        }
        idempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (idempotencyKey?.Length > MaxIdempotencyKeyLength)
        {
            throw new ArgumentException(
                        $"Idempotency key must not exceed {MaxIdempotencyKeyLength} characters.",
                        nameof(idempotencyKey));
        }
        var trackingId = Guid.NewGuid();
        var chainRunId = Guid.NewGuid();
        var protectedPayload = inputPayload is null
            ? null
            : _encryptor.Protect(inputPayload, IDataEncryptor.Purpose.RpcChainSubmission);
        var overridesJson = stepPayloadOverrides is { Count: > 0 }
            ? JsonSerializer.Serialize(stepPayloadOverrides) : null;
        if (overridesJson is { Length: > 262144 })
        {
            throw new ArgumentException("Workflow step inputs exceed 256 KiB.", nameof(stepPayloadOverrides));
        }
        var protectedOverrides = overridesJson is null
                    ? null : _encryptor.Protect(overridesJson, IDataEncryptor.Purpose.RpcChainSubmission);
        // Compute before encrypting; retaining a digest lets completed receipts be
        // checked after the encrypted payload has been cleared.
        var digestSource = JsonSerializer.Serialize(new
        {
            Payload = inputPayload,
            ReplayOriginalRunId = replayOriginalRunId,
            ReplayFromStepIndex = replayFromStepIndex,
            Overrides = stepPayloadOverrides is { Count: > 0 }
                ? stepPayloadOverrides.OrderBy(pair => pair.Key)
                    .Select(pair => new { pair.Key, pair.Value }).ToArray()
                : null
        });
        var requestDigest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(digestSource)));

        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }
        // Source ingestion owns the transaction when its records and messages must commit together.
        await using var ownedTransaction = _db.Database.CurrentTransaction is null ? await connection.BeginTransactionAsync(ct) : null;
        var transaction = ownedTransaction ?? (NpgsqlTransaction)_db.Database.CurrentTransaction!.GetDbTransaction();
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
                    ("Id", "TenantId", "ProjectId", "ChainId", "ChainRunId", "IdempotencyKey", "QueueId",
                     "InputPayload", "StepPayloadOverrides", "RequestDigest", "ReplayOfRunId", "ReplayFromStepIndex", "SubmitterUserId", "SubmitterRole", "Status", "Attempts",
                     "SubmittedAt", "NextAttemptAt", "ExpiresAt", "ReceiptRetentionDays", "MessageTtlSeconds", "MaxAttempts", "RetryDelaySeconds")
                SELECT @id, @tenant, @project, @chain, @run, @key, @queue,
                     @payload, @overrides, @digest, @original, @from, @user, @role, 'Queued', 0, now(), COALESCE(@available, now()), COALESCE(@available, now()) + make_interval(secs => @ttl), @retention, @ttl, @maxAttempts, @retryDelay
                WHERE @canStart
                ON CONFLICT ("TenantId", "IdempotencyKey")
                    WHERE "IdempotencyKey" IS NOT NULL
                DO NOTHING
                RETURNING "Id", "ProjectId", "ChainId", "ChainRunId", "Status", "Attempts",
                          "LastError", "SubmittedAt", "StartedAt", "FinishedAt", "InputPayload", "StepPayloadOverrides", "RequestDigest"
            )
            SELECT * FROM inserted
            UNION ALL
            SELECT "Id", "ProjectId", "ChainId", "ChainRunId", "Status", "Attempts",
                   "LastError", "SubmittedAt", "StartedAt", "FinishedAt", "InputPayload", "StepPayloadOverrides", "RequestDigest"
            FROM rpc_chain_submissions
            WHERE @key IS NOT NULL AND "TenantId" = @tenant AND "IdempotencyKey" = @key
              AND NOT EXISTS (SELECT 1 FROM inserted)
            LIMIT 1
            """;
        command.Parameters.AddWithValue("id", trackingId);
        command.Parameters.AddWithValue("canStart", usage?.CanRun ?? true);
        command.Parameters.AddWithValue("tenant", _tenant.TenantId);
        command.Parameters.AddWithValue("project", projectId);
        command.Parameters.AddWithValue("chain", chainId);
        command.Parameters.AddWithValue("run", chainRunId);
        command.Parameters.AddWithValue("key", (object?)idempotencyKey ?? DBNull.Value);
        command.Parameters.AddWithValue("queue", NpgsqlTypes.NpgsqlDbType.Uuid, (object?)queueId ?? DBNull.Value);
        command.Parameters.AddWithValue("ttl", NpgsqlTypes.NpgsqlDbType.Integer, (object?)messageTtlSeconds ?? DBNull.Value);
        command.Parameters.AddWithValue("retention", receiptRetentionDays);
        command.Parameters.AddWithValue("maxAttempts", maxAttempts);
        command.Parameters.AddWithValue("retryDelay", retryDelaySeconds);
        command.Parameters.AddWithValue("available", NpgsqlTypes.NpgsqlDbType.TimestampTz, (object?)availableAt?.ToUniversalTime() ?? DBNull.Value);
        command.Parameters.AddWithValue("payload", (object?)protectedPayload ?? DBNull.Value);
        command.Parameters.AddWithValue("overrides", (object?)protectedOverrides ?? DBNull.Value);
        command.Parameters.AddWithValue("digest", requestDigest);
        command.Parameters.AddWithValue("original", NpgsqlTypes.NpgsqlDbType.Uuid,
            (object?)replayOriginalRunId ?? DBNull.Value);
        command.Parameters.AddWithValue("from", NpgsqlTypes.NpgsqlDbType.Integer,
            (object?)replayFromStepIndex ?? DBNull.Value);
        command.Parameters.AddWithValue("user", _user.UserId);
        command.Parameters.AddWithValue("role", _user.Role);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            if (usage is { CanRun: false })
                throw new TenantUsageLimitException("This workspace has reached its run limit. Change plans or wait for the next billing period.");
            throw new InvalidOperationException("The chain submission could not be persisted.");
        }
        var result = Read(reader);
        var storedPayload = reader.IsDBNull(10) ? null :
            _encryptor.Unprotect(reader.GetString(10), IDataEncryptor.Purpose.RpcChainSubmission);
        var storedOverrides = reader.IsDBNull(11) ? null :
            _encryptor.Unprotect(reader.GetString(11), IDataEncryptor.Purpose.RpcChainSubmission);
        var storedDigest = reader.IsDBNull(12) ? null : reader.GetString(12);
        await reader.DisposeAsync();
        if (result.ProjectId != projectId || result.ChainId != chainId)
        {
            throw new InvalidOperationException("This idempotency key already belongs to another workflow.");
        }        // New receipts compare the digest even after ciphertext is scrubbed.
        // Pre-digest receipts use retained encrypted inputs for comparison.
        void ValidateRequest()
        {
            if (storedDigest is not null)
            {
                if (!string.Equals(storedDigest, requestDigest, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                                    "This idempotency key was submitted with different workflow inputs.");
                }
                return;
            }
            if (!string.Equals(storedPayload, inputPayload, StringComparison.Ordinal)
                        || (storedOverrides is null) != (overridesJson is null)
                        || (storedOverrides is not null && !JsonNode.DeepEquals(
                            JsonNode.Parse(storedOverrides), JsonNode.Parse(overridesJson!))))
            {
                throw new InvalidOperationException(
                            "This legacy idempotency key was submitted with different workflow inputs.");
            }
        }
        ValidateRequest();
        if (ownedTransaction is not null)
        {
            await ownedTransaction.CommitAsync(ct);
        }
        return result;
    }

    public async Task<JobChainSubmission?> GetAsync(Guid trackingId, CancellationToken ct = default)
    {
        if (!_tenant.IsResolved || trackingId == Guid.Empty)
        {
            return null;
        }
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }
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

    public Task<string> ResumeAsync(Guid chainRunId, Guid inputRequestId, string valuesJson, CancellationToken ct = default)
        => ResumeCoreAsync(chainRunId, inputRequestId, valuesJson, false, ct);

    public Task<string> ResumeAgentAsync(Guid chainRunId, Guid inputRequestId, CancellationToken ct = default)
        => ResumeCoreAsync(chainRunId, inputRequestId, null, true, ct);

    private async Task<string> ResumeCoreAsync(Guid chainRunId, Guid inputRequestId, string? valuesJson, bool automatic, CancellationToken ct)
    {
        if (!_tenant.IsResolved || !_user.IsAuthenticated || !await _permissions.HasAsync(Permission.JobsRun, ct))
        {
            throw new UnauthorizedAccessException("Permission to run jobs is required.");
        }
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var rows = await _db.ChainRuns.FromSqlInterpolated($"""
            SELECT * FROM chain_runs WHERE "Id" = {chainRunId} AND "TenantId" = {_tenant.TenantId} FOR UPDATE
            """).ToListAsync(ct);
        var row = rows.SingleOrDefault() ?? throw new ArgumentException("Chain run not found.");
        // A Blazor circuit can retain a tracked checkpoint while a worker advances to the next input.
        await _db.Entry(row).ReloadAsync(ct);
        var purpose = IDataEncryptor.Purpose.ChainRun;
        var request = row.InputRequest is null ? null : JsonSerializer.Deserialize<ChainInputRequest>(
            _encryptor.Unprotect(row.InputRequest, purpose));
        if (request is null || request.Id != inputRequestId)
        {
            throw new InvalidOperationException("This input request is no longer current.");
        }
        if (automatic)
        {
            var chain = await new EfJobChainRepository(_db).GetByIdAsync(row.ChainId, ct);
            if (chain is null || chain.ProjectId != row.ProjectId || row.ResumeStageIndex is not { } stageIndex
                || stageIndex < 0 || stageIndex >= chain.Stages.Count
                || chain.Stages[stageIndex].Action is not PlaceContext.Domain.ValueObjects.LoopChainAction
                    { AgentProvider: PlaceContext.Domain.ValueObjects.LoopChainAction.AutomaticAgent } action
                || action.Type != request.Kind || request.Steps.Count != 1
                || request.Steps[0].Index != chain.Stages.Take(stageIndex).Sum(stage => stage.ExecutionCount))
                throw new InvalidOperationException("Save automatic execution settings for this paused agent step first.");
        }
        if (request.RunAutomatically)
        {
            if (!automatic) throw new InvalidOperationException("This step was already submitted for automatic execution.");
            return row.Status;
        }
        if (request.SubmittedValues is not null)
        {
            if (automatic) throw new InvalidOperationException("An external response was already submitted.");
            if (!JsonNode.DeepEquals(JsonNode.Parse(request.SubmittedValues), JsonNode.Parse(valuesJson!)))
            {
                throw new InvalidOperationException("Different values were already submitted for this input request.");
            }
            return row.Status;
        }
        if (row.Status != "AwaitingInput")
        {
            throw new InvalidOperationException("Chain run is not waiting for input.");
        }
        if (!automatic)
        {
            var overrides = ChainInputPayload.Apply(request, valuesJson!,
                row.ContinuationOverrides is null ? null : _encryptor.Unprotect(row.ContinuationOverrides, purpose),
                row.FinalOutput is null ? null : _encryptor.Unprotect(row.FinalOutput, purpose));
            row.ContinuationOverrides = _encryptor.Protect(JsonSerializer.Serialize(overrides), purpose);
        }
        row.InitiatedBy ??= _user.UserId;
        row.InputRequest = _encryptor.Protect(JsonSerializer.Serialize(request with
        {
            SubmittedBy = _user.UserId,
            SubmittedValues = valuesJson,
            RunAutomatically = automatic
        }), purpose);
        row.Status = "Waiting";
        row.ResumeAt = DateTimeOffset.UtcNow;
        row.ContinuationClaimedAt = null;
        row.ContinuationClaimedBy = null;
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return row.Status;
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
