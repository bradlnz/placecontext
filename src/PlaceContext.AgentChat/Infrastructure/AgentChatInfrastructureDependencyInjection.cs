using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.AgentChat.Infrastructure.Caching;
using PlaceContext.AgentChat.Infrastructure.Chat;
using PlaceContext.AgentChat.Infrastructure.Persistence;
using PlaceContext.AgentChat.Infrastructure.Slack;
using PlaceContext.AgentChat.Infrastructure.Tenancy;
using PlaceContext.AgentChat.Infrastructure.Integration;
using PlaceContext.AgentChat.Integration;
using PlaceContext.AgentChat.Slack;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.AgentChat;

public static class AgentChatInfrastructureDependencyInjection
{
    public static IServiceCollection AddAgentChatInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AgentChat")
            ?? configuration[$"{AgentChatPersistenceOptions.SectionName}:ConnectionString"]
            ?? configuration["PlaceContext:ConnectionString"]
            ?? AgentChatPersistenceOptions.DefaultConnectionString;

        services.Configure<AgentChatPersistenceOptions>(
            configuration.GetSection(AgentChatPersistenceOptions.SectionName));
        services.AddDbContext<AgentChatDbContext>(options =>
        {
            options.UseNpgsql(connectionString, postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory_AgentChat"));
            options.ConfigureWarnings(warnings =>
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        });
        services.AddHealthChecks()
            .AddDbContextCheck<AgentChatDbContext>("agent-chat-database");
        services.AddScoped<IAgentChatUnitOfWork>(provider =>
            provider.GetRequiredService<AgentChatDbContext>());

        var redisConnection = configuration["PlaceContext:Redis:ConnectionString"];
        Type innerMemoryStoreType;
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnection;
                options.InstanceName = "pc";
            });
            services.AddSingleton<RedisChatMemoryStore>();
            services.AddSingleton<IChatMemoryStore>(provider =>
                provider.GetRequiredService<RedisChatMemoryStore>());
            innerMemoryStoreType = typeof(RedisChatMemoryStore);
        }
        else
        {
            services.AddSingleton<NullChatMemoryStore>();
            services.AddSingleton<IChatMemoryStore>(provider =>
                provider.GetRequiredService<NullChatMemoryStore>());
            innerMemoryStoreType = typeof(NullChatMemoryStore);
        }

        var qdrantUrl = configuration["PlaceContext:Qdrant:Endpoint"];
        if (!string.IsNullOrWhiteSpace(qdrantUrl))
        {
            var collection = configuration["PlaceContext:Qdrant:Collection"] ?? "chat-memory";
            services.AddSingleton<IChatMemoryStore>(provider =>
            {
                var fallback = (IChatMemoryStore)provider.GetRequiredService(innerMemoryStoreType);
                var embeddings = provider.GetRequiredService<IEmbeddingGateway>();
                var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient();
                return new QdrantChatMemoryStore(fallback, embeddings, http, qdrantUrl, collection);
            });
        }
        services.AddScoped<IAgentSessionStore, ChatMemoryAgentSessionStore>();

        services.Configure<SlackOptions>(configuration.GetSection(SlackOptions.SectionName));
        var slackOptions = configuration.GetSection(SlackOptions.SectionName).Get<SlackOptions>()
            ?? new SlackOptions();
        if (slackOptions.IsConfigured)
            services.AddSingleton<ISlackClient, SlackApiClient>();
        else
            services.AddSingleton<ISlackClient, NullSlackClient>();
        if (!string.IsNullOrWhiteSpace(redisConnection))
            services.AddSingleton<ISlackThreadSessionStore, DistributedCacheSlackThreadSessionStore>();
        else
            services.AddSingleton<ISlackThreadSessionStore, MemorySlackThreadSessionStore>();

        var clusterEndpoint = configuration["PlaceContext:ClusterChat:Endpoint"];
        var ollamaEndpoint = configuration["PlaceContext:Chat:Endpoint"];
        if (!string.IsNullOrWhiteSpace(clusterEndpoint))
            services.AddSingleton<IChatGateway, ClusterChatGateway>();
        else if (!string.IsNullOrWhiteSpace(ollamaEndpoint))
            services.AddSingleton<IChatGateway, OllamaChatGateway>();
        else
            services.AddSingleton<IChatGateway, NullChatGateway>();

        services.AddScoped<IAgentConfigRepository, EfAgentConfigRepository>();
        services.AddScoped<IAgentChatSessionRepository, EfAgentChatSessionRepository>();
        services.AddScoped<IChatCommandRepository, EfChatCommandRepository>();
        services.AddHttpClient();
        services.AddScoped<IAgentChatWorkspaceClient, HttpAgentChatWorkspaceClient>();
        var identityAddress = configuration["PlaceContext:AgentChat:Identity:BaseAddress"]
            ?? configuration["PlaceContext:Microservices:Destinations:Identity"];
        if (!string.IsNullOrWhiteSpace(identityAddress))
            services.AddScoped<IRequestTenantResolver, HttpIdentityTenantResolver>();
        return services;
    }
}
