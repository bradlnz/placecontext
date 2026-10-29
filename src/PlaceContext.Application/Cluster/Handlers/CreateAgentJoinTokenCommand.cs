using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Cluster;

public sealed record CreateAgentJoinTokenCommand : ICommand<string>;
