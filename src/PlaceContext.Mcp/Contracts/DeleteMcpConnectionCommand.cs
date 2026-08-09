using System.Text.Json;
using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

public sealed record DeleteMcpConnectionCommand(Guid Id) : ICommand<bool>;
