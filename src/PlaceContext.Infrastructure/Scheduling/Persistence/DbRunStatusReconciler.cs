using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Secrets;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Scheduling;

/// <summary>Reconcile failed jobs with abandoned workflow snapshots without replaying workloads.</summary>
public sealed class DbRunStatusReconciler(AppDbContext db, IDataEncryptor encryptor, ILogger<DbRunStatusReconciler>? log = null)
{
    public async Task<int> ReconcileAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var grace = now.AddMinutes(-2);
        var repaired = 0;
        var after = Guid.Empty;
        while (true)
        {
            var rows = await db.ChainRuns.FromSqlInterpolated($"""
                SELECT r.* FROM chain_runs r
                WHERE r."Status" IN ('Running', 'Failed') AND r."Id" > {after}
                  AND NOT EXISTS (SELECT 1 FROM rpc_chain_submissions s
                      WHERE s."TenantId" = r."TenantId" AND s."ChainRunId" = r."Id"
                        AND s."Status" = 'Running' AND s."ClaimedBy" IS NOT NULL AND s."HeartbeatAt" > {grace})
                  AND NOT EXISTS (SELECT 1 FROM tenant_execution_leases l
                      WHERE l."TenantId" = r."TenantId" AND l."RunKey" = 'workflow:' || replace(r."Id"::text, '-', '')
                        AND l."ExpiresAt" > {now})
                  AND (r."ContinuationClaimedAt" IS NULL OR r."ContinuationClaimedAt" < {now.AddMinutes(-5)})
                """).AsNoTracking().OrderBy(r => r.Id).Take(100).ToListAsync(ct);
            if (rows.Count == 0) return repaired;
            after = rows[^1].Id;
            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var plaintext = encryptor.Unprotect(row.StepsJson, IDataEncryptor.Purpose.ChainRun);
                    if (string.IsNullOrWhiteSpace(plaintext)) continue;
                    var steps = JsonNode.Parse(plaintext)!.AsArray();
                    var running = steps.Where(s => s?["status"]?.GetValue<string>() == "Running").ToList();
                    if (running.Count == 0 && row.Status == "Running")
                        running = steps.Where(s => s?["status"]?.GetValue<string>() == "Failed").ToList();
                    if (running.Count == 0 || running.Any(s => s?["runId"] is null)) continue;
                    var ids = running.Select(s => Guid.Parse(s!["runId"]!.GetValue<string>())).ToArray();
                    var attempts = await db.JobRuns.AsNoTracking()
                        .Where(j => ids.Contains(j.Id) || j.OriginalRunId != null && ids.Contains(j.OriginalRunId.Value))
                        .Select(j => new { j.Id, j.OriginalRunId, j.Status, j.FinishedAt, j.JobId, j.AttemptNumber })
                        .ToListAsync(ct);
                    var latest = ids.Select(id => attempts.Where(j => j.Id == id || j.OriginalRunId == id)
                        .OrderByDescending(j => j.AttemptNumber).FirstOrDefault()).ToList();
                    // Never interrupt a live sibling, retry, or completed job awaiting output ingestion.
                    if (latest.Any(j => j is null || j.Status is "Running" or "Queued" || j.FinishedAt is null)
                        || !latest.Any(j => j!.Status is "Failed" or "Cancelled")) continue;
                    var jobIds = latest.Select(j => j!.JobId).Distinct().ToArray();
                    var delays = await db.Jobs.AsNoTracking().Where(j => jobIds.Contains(j.Id))
                        .Select(j => new { j.Id, j.RetryDelaySeconds })
                        .ToDictionaryAsync(j => j.Id, j => j.RetryDelaySeconds, ct);
                    if (latest.Any(j => j!.FinishedAt > grace.AddSeconds(-delays.GetValueOrDefault(j.JobId)))) continue;
                    var status = row.Status == "Failed" || latest.Any(j => j!.Status == "Failed")
                        ? "Failed" : "Cancelled";
                    for (var i = 0; i < running.Count; i++)
                    {
                        running[i]!["status"] = latest[i]!.Status;
                        running[i]!["runId"] = latest[i]!.Id.ToString();
                        running[i]!["finishedAt"] = latest[i]!.FinishedAt;
                        running[i]!["error"] = latest[i]!.Status == "Failed"
                            ? "Job failed; workflow status corrected by the reconciliation job." : null;
                    }
                    foreach (var step in steps.Where(s => s?["status"]?.GetValue<string>() == "Pending"))
                    {
                        step!["status"] = status == "Cancelled" ? "Cancelled" : "Skipped";
                        step["finishedAt"] = now;
                    }
                    var protectedSteps = encryptor.Protect(steps.ToJsonString(), IDataEncryptor.Purpose.ChainRun);
                    // Compare the snapshot so a concurrent driver or another replica wins safely.
                    var changed = await db.ChainRuns.Where(r => r.Id == row.Id && r.Status == row.Status && r.StepsJson == row.StepsJson)
                        .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, status)
                            .SetProperty(r => r.StepsJson, protectedSteps).SetProperty(r => r.FinishedAt, now), ct);
                    if (changed == 0) continue;
                    repaired += changed;
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        UPDATE rpc_chain_submissions SET "Status" = {status}, "FinishedAt" = {now}
                        WHERE "TenantId" = {row.TenantId} AND "ChainRunId" = {row.Id} AND "Status" = 'Running'
                          AND ("QueueId" IS NULL OR {status} = 'Cancelled')
                        """, ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    log?.LogWarning(ex, "Could not reconcile workflow {RunId}; continuing the sweep.", row.Id);
                }
            }
        }
    }
}
