using System.Data;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Jobs;
using PlaceContext.Application.Secrets;
using PlaceContext.Application.Tenancy;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Scheduling;
using k8s.Autorest;
using k8s;
using PlaceContext.Application.Access;

namespace PlaceContext.Infrastructure.Workload;

public sealed class ClusterAppQueueGateway(IKubernetes client, ICurrentTenant tenant, DbChainQueueService queues,
    AppDbContext db, IDataEncryptor encryptor, IPermissionService permissions) : IAppQueueGateway
{
    private async Task<Guid> AuthorizeAsync(string credential, Guid queueId, CancellationToken ct)
    {
        if (!tenant.IsResolved || credential.Length != 101 || !credential.StartsWith("pca_", StringComparison.Ordinal)
            || credential[36] != '.' || !Guid.TryParseExact(credential.Substring(4, 32), "N", out var app))
        {
            throw new UnauthorizedAccessException();
        }
        try
        {
            var ns = await client.CoreV1.ReadNamespaceAsync(ClusterServiceConsole.AppNamespace(app), cancellationToken: ct);
            if (!Guid.TryParse(ns.Metadata.Labels[ClusterServiceConsole.Project], out var project)
                || !ClusterServiceConsole.IsOwned(ns, tenant.TenantId, project, app, "service")
                || !ns.Metadata.Annotations.TryGetValue(ClusterServiceConsole.Queues, out var grants)
                || !grants.Split(',').Contains(queueId.ToString("D"), StringComparer.Ordinal))
            {
                throw new UnauthorizedAccessException();
            }
            var secret = await client.CoreV1.ReadNamespacedSecretAsync("app-environment", ns.Metadata.Name, cancellationToken: ct);
            if (!secret.Data.TryGetValue("PLACECONTEXT_API_TOKEN", out var expected)
                || !CryptographicOperations.FixedTimeEquals(expected, Encoding.UTF8.GetBytes(credential)))
            {
                throw new UnauthorizedAccessException();
            }
            var queue = await queues.GetQueueAsync(queueId, ct);
            if (queue?.ProjectId != project)
            {
                throw new UnauthorizedAccessException();
            }
            return app;
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new UnauthorizedAccessException();
        }
    }
    public async Task<JobChainSubmission> SubmitAsync(string credential, Guid queueId, JsonElement payload, string idempotencyKey, CancellationToken ct)
    {
        var app = await AuthorizeAsync(credential, queueId, ct);
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 64 || idempotencyKey.Any(char.IsControl))
        {
            throw new ArgumentException("An Idempotency-Key of 1–64 characters is required.");
        }
        var identity = await db.Users.AsNoTracking().FirstOrDefaultAsync(user => user.Id == app, ct) ?? throw new UnauthorizedAccessException();
        CurrentUser.Set(new(identity.Id, identity.Role));
        try
        {
            if (!await permissions.HasAsync(Permission.JobsRun, ct))
            {
                throw new UnauthorizedAccessException();
            }
            return await queues.SubmitAuthorizedAsync(queueId, payload, $"app:{app:N}:{idempotencyKey}", null, ct);
        }
        finally
        {
            CurrentUser.Clear();
        }
    }
    public async Task<AppQueueResult?> GetAsync(string credential, Guid queueId, Guid trackingId, CancellationToken ct)
    {
        var app = await AuthorizeAsync(credential, queueId, ct);
        // Receipts from another app remain private even when both apps can submit to the same queue.
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1 FROM rpc_chain_submissions WHERE "TenantId" = @tenant AND "QueueId" = @queue
            AND "Id" = @tracking AND "IdempotencyKey" LIKE @prefix
            """;
        foreach (var (key, value) in new Dictionary<string, object> { ["tenant"] = tenant.TenantId, ["queue"] = queueId, ["tracking"] = trackingId, ["prefix"] = $"queue:{queueId:N}:app:{app:N}:%" })
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = key;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
        if (await command.ExecuteScalarAsync(ct) is null)
        {
            return null;
        }
        var receipt = await queues.GetAuthorizedAsync(queueId, trackingId, ct);
        if (receipt is null)
        {
            return null;
        }
        var run = await db.ChainRuns.AsNoTracking().FirstOrDefaultAsync(item => item.Id == receipt.ChainRunId && item.ProjectId == receipt.ProjectId && item.ChainId == receipt.ChainId, ct);
        return new(receipt, run?.FinalOutput is { } output ? encryptor.Unprotect(output, IDataEncryptor.Purpose.ChainRun) : null);
    }
}
