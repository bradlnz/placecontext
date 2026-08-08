using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Ports;
using PlaceContext.Jobs.Infrastructure.Persistence;

namespace PlaceContext.Jobs.Infrastructure.Security;

/// <summary>Adopts legacy plaintext Jobs payloads into the shared encrypted wire format.</summary>
public static class JobsEncryptionAtRestBootstrap
{
    public static async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var db = provider.GetRequiredService<JobsDbContext>();
        var encryptor = provider.GetRequiredService<IDataEncryptor>();
        var logger = provider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("JobsEncryptionAtRestBootstrap");

        var rewritten = 0;
        rewritten += await EncryptJobsAsync(db, encryptor, cancellationToken);
        rewritten += await EncryptRunsAsync(db, encryptor, cancellationToken);
        rewritten += await EncryptChainsAsync(db, encryptor, cancellationToken);
        rewritten += await EncryptEventsAsync(db, encryptor, cancellationToken);
        rewritten += await EncryptPendingRunsAsync(db, encryptor, cancellationToken);

        logger.LogInformation(
            "Jobs encryption-at-rest bootstrap rewrote {Count} field(s).",
            rewritten);
    }

    private static bool NeedsProtection(IDataEncryptor encryptor, string? value)
        => !string.IsNullOrEmpty(value) && !encryptor.IsProtected(value);

    private static async Task<int> EncryptJobsAsync(
        JobsDbContext db,
        IDataEncryptor encryptor,
        CancellationToken cancellationToken)
    {
        var rows = await db.Jobs.IgnoreQueryFilters().ToListAsync(cancellationToken);
        var count = 0;
        foreach (var row in rows)
        {
            row.MapSource = Protect(row.MapSource);
            row.MapFilesJson = Protect(row.MapFilesJson);
            row.InputPayloadsJson = Protect(row.InputPayloadsJson)!;
            row.MapEnvJson = Protect(row.MapEnvJson)!;
            row.ReduceSource = Protect(row.ReduceSource);
            row.ReduceFilesJson = Protect(row.ReduceFilesJson);
            row.ReduceEnvJson = Protect(row.ReduceEnvJson);
        }
        if (count > 0) await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return count;

        string? Protect(string? value)
        {
            if (!NeedsProtection(encryptor, value)) return value;
            count++;
            return encryptor.Protect(value!, DataEncryptionPurpose.JobSource);
        }
    }

    private static async Task<int> EncryptRunsAsync(
        JobsDbContext db,
        IDataEncryptor encryptor,
        CancellationToken cancellationToken)
    {
        var rows = await db.JobRuns.IgnoreQueryFilters().ToListAsync(cancellationToken);
        var count = 0;
        foreach (var row in rows)
        {
            if (NeedsProtection(encryptor, row.ShardResultsJson))
            {
                row.ShardResultsJson = encryptor.Protect(row.ShardResultsJson, DataEncryptionPurpose.JobRun);
                count++;
            }
            if (NeedsProtection(encryptor, row.ReduceResultJson))
            {
                row.ReduceResultJson = encryptor.Protect(row.ReduceResultJson!, DataEncryptionPurpose.JobRun);
                count++;
            }
            if (NeedsProtection(encryptor, row.SnapshotJson))
            {
                row.SnapshotJson = encryptor.Protect(row.SnapshotJson, DataEncryptionPurpose.JobRun);
                count++;
            }
        }
        if (count > 0) await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return count;
    }

    private static async Task<int> EncryptChainsAsync(
        JobsDbContext db,
        IDataEncryptor encryptor,
        CancellationToken cancellationToken)
    {
        var rows = await db.ChainRuns.IgnoreQueryFilters().ToListAsync(cancellationToken);
        var count = 0;
        foreach (var row in rows)
        {
            if (NeedsProtection(encryptor, row.StepsJson))
            {
                row.StepsJson = encryptor.Protect(row.StepsJson, DataEncryptionPurpose.ChainRun);
                count++;
            }
            if (NeedsProtection(encryptor, row.FinalOutput))
            {
                row.FinalOutput = encryptor.Protect(row.FinalOutput!, DataEncryptionPurpose.ChainRun);
                count++;
            }
            if (NeedsProtection(encryptor, row.ContinuationOverrides))
            {
                row.ContinuationOverrides = encryptor.Protect(
                    row.ContinuationOverrides!, DataEncryptionPurpose.ChainRun);
                count++;
            }
        }
        if (count > 0) await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return count;
    }

    private static async Task<int> EncryptEventsAsync(
        JobsDbContext db,
        IDataEncryptor encryptor,
        CancellationToken cancellationToken)
    {
        var rows = await db.EventOccurrences.IgnoreQueryFilters().ToListAsync(cancellationToken);
        var count = 0;
        foreach (var row in rows)
        {
            if (!NeedsProtection(encryptor, row.Payload)) continue;
            row.Payload = encryptor.Protect(row.Payload!, DataEncryptionPurpose.EventPayload);
            count++;
        }
        if (count > 0) await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return count;
    }

    private static async Task<int> EncryptPendingRunsAsync(
        JobsDbContext db,
        IDataEncryptor encryptor,
        CancellationToken cancellationToken)
    {
        var rows = await db.PendingRuns.ToListAsync(cancellationToken);
        var count = 0;
        foreach (var row in rows)
        {
            if (!NeedsProtection(encryptor, row.Payload)) continue;
            row.Payload = encryptor.Protect(row.Payload!, DataEncryptionPurpose.PendingRun);
            count++;
        }
        if (count > 0) await db.SaveChangesAsync(cancellationToken);
        return count;
    }
}
