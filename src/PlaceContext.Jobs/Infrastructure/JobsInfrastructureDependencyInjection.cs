using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PlaceContext.Jobs.Domain.Persistence;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Jobs.Infrastructure.Caching;
using PlaceContext.Jobs.Infrastructure.Observability;
using PlaceContext.Jobs.Infrastructure.Persistence;
using PlaceContext.Jobs.Infrastructure.Scheduling;
using PlaceContext.Jobs.Infrastructure.Security;
using PlaceContext.Jobs.Infrastructure.Workload;

namespace PlaceContext.Jobs;

public static class JobsInfrastructureDependencyInjection
{
    public static IServiceCollection AddJobsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Jobs")
            ?? configuration[$"{JobsPersistenceOptions.SectionName}:ConnectionString"]
            ?? configuration["PlaceContext:ConnectionString"]
            ?? JobsPersistenceOptions.DefaultConnectionString;

        services.Configure<JobsPersistenceOptions>(options =>
            options.ConnectionString = connectionString);
        services.AddDbContext<JobsDbContext>(options =>
        {
            options.UseNpgsql(connectionString, postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory_Jobs"));
            options.ConfigureWarnings(warnings =>
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        });
        services.AddHealthChecks()
            .AddCheck<JobsDatabaseHealthCheck>("jobs-database");
        services.AddScoped<IJobsUnitOfWork>(provider =>
            provider.GetRequiredService<JobsDbContext>());
        services.AddDataProtection().SetApplicationName("placecontext");
        services.TryAddSingleton<IDataEncryptor, JobsDataProtectionEncryptor>();

        services.Configure<WorkloadRunnerOptions>(
            configuration.GetSection("PlaceContext:WorkloadRunner"));
        services.AddSingleton<IWorkloadOutputBuffer, InMemoryWorkloadOutputBuffer>();
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST")))
            services.AddSingleton<IWorkloadRunner, KubernetesWorkloadRunner>();
        else
            services.AddSingleton<IWorkloadRunner, DockerWorkloadRunner>();

        var redisConnection = configuration["PlaceContext:Redis:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnection;
                options.InstanceName = "pc";
            });
            services.AddSingleton<IJobRunCache, RedisJobRunCache>();
            services.AddSingleton<IChainContextStore, RedisChainContextStore>();
        }
        else
        {
            services.AddSingleton<IJobRunCache, NullJobRunCache>();
            services.AddSingleton<IChainContextStore, NullChainContextStore>();
        }

        services.AddScoped<IJobRepository, EfJobRepository>();
        services.AddScoped<IJobRunRepository, EfJobRunRepository>();
        services.AddScoped<IJobTestStore, EfJobTestStore>();
        services.AddScoped<IJobTriggerRepository, EfJobTriggerRepository>();
        services.AddScoped<IJobChainRepository, EfJobChainRepository>();
        services.AddScoped<IChainRunRepository, EfChainRunRepository>();
        services.AddScoped<IEventRepository, EfEventRepository>();
        services.AddScoped<IRunStatusReader, DbRunStatusReader>();

        services.AddSingleton<ICronSchedule, CronosCronSchedule>();
        services.AddScoped<IJobRunQueue, DbJobRunQueue>();
        services.AddHostedService<TriggerSchedulerService>();
        services.AddHostedService<ChainContinuationWorker>();
        services.AddHostedService<RunStatusWatcherService>();

        services.AddSingleton<JobTelemetryCollector>();
        services.AddSingleton<IJobTelemetryReader>(provider =>
            provider.GetRequiredService<JobTelemetryCollector>());
        services.AddHostedService<JobTelemetryCollectorStartup>();
        return services;
    }
}
