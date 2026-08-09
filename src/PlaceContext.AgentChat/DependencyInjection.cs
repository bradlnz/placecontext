using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Agents.Services;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;

namespace PlaceContext.AgentChat;

public static class DependencyInjection
{
    public static IServiceCollection AddAgentChatApi(this IServiceCollection services)
    {
        services.AddScoped<ICommandHandler<CreateChatCommandCommand, ChatCommandView>, CreateChatCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateChatCommandCommand, ChatCommandView>, UpdateChatCommandHandler>();
        services.AddScoped<ICommandHandler<DeleteChatCommandCommand, bool>, DeleteChatCommandHandler>();
        services.AddScoped<IQueryHandler<ListChatCommandsQuery, IReadOnlyList<ChatCommandView>>, ListChatCommandsHandler>();
        services.AddScoped<IQueryHandler<GetAgentConfigQuery, AgentConfigView>, GetAgentConfigHandler>();
        services.AddScoped<IQueryHandler<ListAgentChatSessionsQuery, IReadOnlyList<AgentChatSessionView>>, ListAgentChatSessionsHandler>();
        services.AddScoped<IQueryHandler<GetAgentChatSessionQuery, AgentChatSessionView?>, GetAgentChatSessionHandler>();
        return services;
    }

    public static IServiceCollection AddAgentChatModule(this IServiceCollection services)
    {
        services.AddScoped<ICommandHandler<CreateChatCommandCommand, ChatCommandView>, CreateChatCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateChatCommandCommand, ChatCommandView>, UpdateChatCommandHandler>();
        services.AddScoped<ICommandHandler<DeleteChatCommandCommand, bool>, DeleteChatCommandHandler>();
        services.AddScoped<IQueryHandler<ListChatCommandsQuery, IReadOnlyList<ChatCommandView>>, ListChatCommandsHandler>();
        services.AddScoped<ICommandHandler<UpdateAgentConfigCommand, AgentConfigView>, UpdateAgentConfigHandler>();
        services.AddScoped<ICommandHandler<SendAgentMessageCommand, AgentChatSessionView>, SendAgentMessageHandler>();
        services.AddScoped<IQueryHandler<GetAgentConfigQuery, AgentConfigView>, GetAgentConfigHandler>();
        services.AddScoped<IQueryHandler<ListAgentChatSessionsQuery, IReadOnlyList<AgentChatSessionView>>, ListAgentChatSessionsHandler>();
        services.AddScoped<IQueryHandler<GetAgentChatSessionQuery, AgentChatSessionView?>, GetAgentChatSessionHandler>();
        services.AddScoped<AgentContextBuilder>();
        services.AddScoped<LaunchpadToolExecutor>();
        services.AddScoped<AgentSessionRunner>();
        services.AddScoped<ILaunchpadRunner>(provider =>
            provider.GetRequiredService<AgentSessionRunner>());
        services.AddScoped<SlackAgentBridge>();
        return services;
    }
}
