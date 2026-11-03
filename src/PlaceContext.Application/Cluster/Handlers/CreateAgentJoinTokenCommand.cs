using PlaceContext.Application.Cqrs;
namespace PlaceContext.Application.Cluster;

public sealed record CreateAgentJoinTokenCommand : ICommand<string>;
