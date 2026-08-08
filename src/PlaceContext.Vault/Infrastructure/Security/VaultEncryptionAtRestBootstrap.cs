using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Ports;
using PlaceContext.Vault.Infrastructure.Persistence;

namespace PlaceContext.Vault.Infrastructure.Security;

/// <summary>Upgrades accidental legacy plaintext Vault rows without crossing context boundaries.</summary>
public static class VaultEncryptionAtRestBootstrap
{
    public static async Task RunAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var serviceProvider = scope.ServiceProvider;
        var dbContext = serviceProvider.GetRequiredService<VaultDbContext>();
        var protector = serviceProvider.GetRequiredService<ISecretProtector>();
        var logger = serviceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(VaultEncryptionAtRestBootstrap));

        var rows = await dbContext.ProjectSecrets
            .IgnoreQueryFilters()
            .ToListAsync(cancellationToken);
        var updated = 0;

        foreach (var row in rows)
        {
            // Historical Vault values used raw Data Protection payloads. Only values that look
            // like accidental plaintext should be wrapped with the current pcenc1 format.
            if (row.Cipher.StartsWith("pcenc1.", StringComparison.Ordinal)
                || !LooksLikeAccidentalPlaintext(row.Cipher))
                continue;

            row.Cipher = protector.Protect(row.Cipher);
            updated++;
        }

        if (updated == 0)
            return;

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Vault encryption bootstrap rewrote {Count} legacy value(s).", updated);
    }

    private static bool LooksLikeAccidentalPlaintext(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 512)
            return false;

        return value.Any(char.IsWhiteSpace)
            || value.All(character => char.IsLetterOrDigit(character) || "-_.".Contains(character));
    }
}
