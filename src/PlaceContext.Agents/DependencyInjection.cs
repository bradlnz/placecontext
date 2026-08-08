using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Agents.Contracts.Commands;
using PlaceContext.Agents.Contracts.Dtos;
using PlaceContext.Agents.Contracts.Queries;
using PlaceContext.Agents.Handlers;
using PlaceContext.Application.Cqrs;

namespace PlaceContext.Agents;

public static class DependencyInjection
{
    public static IServiceCollection AddAgentsApi(this IServiceCollection services)
    {
        services.AddScoped<ICommandHandler<CreateAgentProfileCommand, AgentProfileView>, CreateAgentProfileHandler>();
        services.AddScoped<ICommandHandler<UpdateAgentProfileCommand, AgentProfileView?>, UpdateAgentProfileHandler>();
        services.AddScoped<ICommandHandler<CreateStaffMemberCommand, StaffMemberView>, CreateStaffMemberHandler>();
        services.AddScoped<ICommandHandler<SetStaffStatusCommand, StaffMemberView?>, SetStaffStatusHandler>();
        services.AddScoped<ICommandHandler<CreateAgentAssignmentCommand, AgentAssignmentView>, CreateAgentAssignmentHandler>();
        services.AddScoped<ICommandHandler<ResolveAgentApprovalCommand, AgentApprovalView?>, ResolveAgentApprovalHandler>();
        services.AddScoped<IQueryHandler<GetAgentsWorkspaceQuery, AgentsWorkspaceView>, GetAgentsWorkspaceHandler>();
        return services;
    }

    public static IServiceCollection AddAgentsModule(this IServiceCollection services)
        => services.AddAgentsApi();
}
