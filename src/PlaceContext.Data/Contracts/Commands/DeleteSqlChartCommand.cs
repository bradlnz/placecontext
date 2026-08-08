using System.Text.Json.Nodes;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

/// <summary>Removes a user-defined SQL chart.</summary>
public sealed record DeleteSqlChartCommand(Guid ProjectId, string Name) : ICommand<bool>;
