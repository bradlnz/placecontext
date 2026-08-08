using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Ports;
using PlaceContext.Crm.Infrastructure.Persistence;

namespace PlaceContext.Crm.Infrastructure.Security;

/// <summary>Bounded, idempotent encryption upgrade for legacy CRM customer data.</summary>
public static class CrmEncryptionAtRestBootstrap
{
    private const int BatchSize = 200;

    public static async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var db = provider.GetRequiredService<CrmDbContext>();
        var encryptor = provider.GetRequiredService<IDataEncryptor>();
        var logger = provider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("CrmEncryptionAtRestBootstrap");

        var count = 0;
        count += await EncryptClientsAsync(db, encryptor, cancellationToken);
        count += await EncryptCommunicationsAsync(db, encryptor, cancellationToken);
        count += await EncryptArtifactMetadataAsync(db, encryptor, cancellationToken);
        count += await EncryptAutomationErrorsAsync(db, encryptor, cancellationToken);

        if (count > 0)
            logger.LogInformation("CRM encryption-at-rest bootstrap rewrote {Count} field(s).", count);
    }

    private static bool NeedsProtection(IDataEncryptor encryptor, string? value)
        => !string.IsNullOrEmpty(value) && !encryptor.IsProtected(value);

    private static async Task<int> EncryptClientsAsync(
        CrmDbContext db,
        IDataEncryptor encryptor,
        CancellationToken cancellationToken)
    {
        var count = 0;
        while (true)
        {
            var rows = await db.CrmClients.IgnoreQueryFilters()
                .Where(row => (row.Name != "" && !row.Name.StartsWith("pcenc1."))
                    || (row.Company != null && row.Company != "" && !row.Company.StartsWith("pcenc1."))
                    || (row.Email != null && row.Email != "" && !row.Email.StartsWith("pcenc1."))
                    || (row.Phone != null && row.Phone != "" && !row.Phone.StartsWith("pcenc1."))
                    || (row.Notes != null && row.Notes != "" && !row.Notes.StartsWith("pcenc1.")))
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (rows.Count == 0) return count;

            foreach (var row in rows)
            {
                row.Name = Protect(row.Name, DataEncryptionPurpose.CrmClient)!;
                row.Company = Protect(row.Company, DataEncryptionPurpose.CrmClient);
                row.Email = Protect(row.Email, DataEncryptionPurpose.CrmClient);
                row.Phone = Protect(row.Phone, DataEncryptionPurpose.CrmClient);
                row.Notes = Protect(row.Notes, DataEncryptionPurpose.CrmClient);
            }
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }

        string? Protect(string? value, string purpose)
        {
            if (!NeedsProtection(encryptor, value)) return value;
            count++;
            return encryptor.Protect(value!, purpose);
        }
    }

    private static async Task<int> EncryptCommunicationsAsync(
        CrmDbContext db,
        IDataEncryptor encryptor,
        CancellationToken cancellationToken)
    {
        var count = 0;
        while (true)
        {
            var rows = await db.CrmCommunications.IgnoreQueryFilters()
                .Where(row => (row.BodyProtected != "" && !row.BodyProtected.StartsWith("pcenc1."))
                    || (row.SubjectProtected != null && row.SubjectProtected != "" && !row.SubjectProtected.StartsWith("pcenc1."))
                    || (row.RecipientProtected != null && row.RecipientProtected != "" && !row.RecipientProtected.StartsWith("pcenc1."))
                    || (row.ExternalId != null && row.ExternalId != "" && !row.ExternalId.StartsWith("pcenc1."))
                    || (row.ErrorProtected != null && row.ErrorProtected != "" && !row.ErrorProtected.StartsWith("pcenc1.")))
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (rows.Count == 0) return count;

            foreach (var row in rows)
            {
                row.BodyProtected = Protect(row.BodyProtected)!;
                row.SubjectProtected = Protect(row.SubjectProtected);
                row.RecipientProtected = Protect(row.RecipientProtected);
                row.ExternalId = Protect(row.ExternalId);
                row.ErrorProtected = Protect(row.ErrorProtected);
            }
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }

        string? Protect(string? value)
        {
            if (!NeedsProtection(encryptor, value)) return value;
            count++;
            return encryptor.Protect(value!, DataEncryptionPurpose.CrmCommunication);
        }
    }

    private static async Task<int> EncryptArtifactMetadataAsync(
        CrmDbContext db,
        IDataEncryptor encryptor,
        CancellationToken cancellationToken)
    {
        var count = 0;
        while (true)
        {
            var rows = await db.CrmClientArtifacts.IgnoreQueryFilters()
                .Where(row => (row.Title != "" && !row.Title.StartsWith("pcenc1."))
                    || (row.ObjectKey != "" && !row.ObjectKey.StartsWith("pcenc1.")))
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (rows.Count == 0) return count;

            foreach (var row in rows)
            {
                if (NeedsProtection(encryptor, row.Title))
                {
                    row.Title = encryptor.Protect(row.Title, DataEncryptionPurpose.CrmArtifactMetadata);
                    count++;
                }
                if (NeedsProtection(encryptor, row.ObjectKey))
                {
                    row.ObjectKey = encryptor.Protect(row.ObjectKey, DataEncryptionPurpose.CrmArtifactMetadata);
                    count++;
                }
            }
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }
    }

    private static async Task<int> EncryptAutomationErrorsAsync(
        CrmDbContext db,
        IDataEncryptor encryptor,
        CancellationToken cancellationToken)
    {
        var count = 0;
        while (true)
        {
            var rows = await db.CrmAutomationQueue
                .Where(row => row.LastError != null && row.LastError != ""
                    && !row.LastError.StartsWith("pcenc1."))
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (rows.Count == 0) return count;

            foreach (var row in rows)
            {
                if (!NeedsProtection(encryptor, row.LastError)) continue;
                row.LastError = encryptor.Protect(row.LastError!, DataEncryptionPurpose.CrmAutomation);
                count++;
            }
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }
    }
}
