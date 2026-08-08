using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Ports;
using System.Text.Json;

namespace PlaceContext.Jobs.Infrastructure.Persistence;

public static class JobsDatabaseMigrationExtensions
{
    public static async Task MigrateJobsDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<JobsDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
        await ApplyCompatibilityUpdatesAsync(dbContext, scope.ServiceProvider, cancellationToken);
    }

    private static async Task ApplyCompatibilityUpdatesAsync(
        JobsDbContext dbContext,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE jobs ADD COLUMN IF NOT EXISTS "AllowApiInvocation" boolean NOT NULL DEFAULT false;""",
                cancellationToken);
        }
        catch
        {
            // Non-Postgres test providers and databases still being initialized do not need this shim.
        }

        var encryptor = services.GetService<IDataEncryptor>();
        if (encryptor is null) return;

        try
        {
            var rows = await dbContext.JobRuns
                .FromSqlRaw("""SELECT * FROM job_runs WHERE "ShardCount" = 0 AND "ShardResultsJson" IS NOT NULL""")
                .IgnoreQueryFilters()
                .ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                try
                {
                    var json = encryptor.Unprotect(row.ShardResultsJson, DataEncryptionPurpose.JobRun);
                    using var document = JsonDocument.Parse(json);
                    if (document.RootElement.ValueKind != JsonValueKind.Array) continue;

                    var outcomes = document.RootElement.EnumerateArray()
                        .Select(item => item.TryGetProperty("Outcome", out var outcome)
                            ? outcome.GetString()
                            : item.TryGetProperty("outcome", out outcome) ? outcome.GetString() : null)
                        .ToList();
                    row.ShardCount = outcomes.Count;
                    row.SucceededShards = outcomes.Count(value =>
                        string.Equals(value, "Succeeded", StringComparison.OrdinalIgnoreCase));
                    row.PartialShards = outcomes.Count(value =>
                        string.Equals(value, "Partial", StringComparison.OrdinalIgnoreCase));
                    row.FailedShards = outcomes.Count(value =>
                        string.Equals(value, "Failed", StringComparison.OrdinalIgnoreCase));
                }
                catch
                {
                    // Leave legacy rows with malformed payloads untouched.
                }
            }

            if (rows.Count > 0)
                await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Compatibility backfill is best-effort and must not prevent service startup.
        }
    }
}
