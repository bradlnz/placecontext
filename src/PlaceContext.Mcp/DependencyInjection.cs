using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Mcp;

namespace PlaceContext.Mcp;

public static class DependencyInjection
{
    public static IServiceCollection AddMcpModule(this IServiceCollection services)
    {
        services.AddScoped<IMcpClientService, McpClientService>();
        services.AddScoped<ICommandHandler<CreateMcpConnectionCommand, McpConnectionView>, CreateMcpConnectionHandler>();
        services.AddScoped<ICommandHandler<UpdateMcpConnectionCommand, McpConnectionView>, UpdateMcpConnectionHandler>();
        services.AddScoped<ICommandHandler<DeleteMcpConnectionCommand, bool>, DeleteMcpConnectionHandler>();
        services.AddScoped<ICommandHandler<TestMcpConnectionCommand, McpConnectionView>, TestMcpConnectionHandler>();
        services.AddScoped<IQueryHandler<ListMcpConnectionsQuery, IReadOnlyList<McpConnectionView>>, ListMcpConnectionsHandler>();
        return services;
    }
}
