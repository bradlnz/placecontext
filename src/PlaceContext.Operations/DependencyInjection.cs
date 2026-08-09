using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Cqrs;
using PlaceContext.Operations.Backup;
using PlaceContext.Operations.Contracts.Backup;

namespace PlaceContext.Operations;

public static class DependencyInjection
{
    public static IServiceCollection AddOperationsModule(this IServiceCollection services)
    {
        services.AddScoped<IQueryHandler<ExportManifestQuery, BackupManifest>, ExportManifestHandler>();
        services.AddScoped<ICommandHandler<ImportManifestCommand, ImportResultView>, ImportManifestHandler>();
        services.AddScoped<IBackupService, BackupService>();
        return services;
    }
}
