using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Ports;
using PlaceContext.Data.Infrastructure.Persistence;

namespace PlaceContext.Data.Infrastructure.Security;

public static class DataEncryptionAtRestBootstrap
{
    public static async Task RunAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var context = provider.GetRequiredService<DataDbContext>();
        var encryptor = provider.GetRequiredService<IDataEncryptor>();
        var logger = provider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("DataEncryptionAtRestBootstrap");
        var purpose = DataEncryptionPurpose.Chart;
        var count = 0;

        foreach (var row in await context.ProjectCharts.IgnoreQueryFilters().ToListAsync(cancellationToken))
        {
            if (string.IsNullOrEmpty(row.Html) || encryptor.IsProtected(row.Html))
                continue;

            row.Html = encryptor.Protect(row.Html, purpose);
            count++;
        }

        if (count > 0)
            await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Data encryption-at-rest bootstrap rewrote {Count} chart field(s).", count);
    }
}
